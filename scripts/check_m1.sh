#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
temporary=$(mktemp -d)
container_id=''
host_pid=''

cleanup() {
  if [[ -n "$host_pid" ]]; then
    kill "$host_pid" 2>/dev/null || true
    wait "$host_pid" 2>/dev/null || true
  fi
  if [[ -n "$container_id" ]]; then
    docker rm -f "$container_id" >/dev/null 2>&1 || true
  fi
  rm -rf "$temporary"
}
trap cleanup EXIT

repository="$temporary/repository"
git init -q -b main "$repository"
git -C "$repository" config user.name 'QA test'
git -C "$repository" config user.email 'qa@example.test'
printf 'base\n' > "$repository/initial.txt"
git -C "$repository" add initial.txt
git -C "$repository" commit -qm 'base'
base_sha=$(git -C "$repository" rev-parse HEAD)
printf 'feature\n' > "$repository/feature.txt"
git -C "$repository" add feature.txt
git -C "$repository" commit -qm 'head'
head_sha=$(git -C "$repository" rev-parse HEAD)
printf 'later\n' > "$repository/later.txt"
git -C "$repository" add later.txt
git -C "$repository" commit -qm 'later'
later_sha=$(git -C "$repository" rev-parse HEAD)

container_id=$(docker run --rm -d -e POSTGRES_DB=qa -e POSTGRES_USER=qa \
  -e POSTGRES_PASSWORD=qa-local-test -p 127.0.0.1::5432 postgres:17)
db_port=$(docker port "$container_id" 5432/tcp | sed 's/.*://')
for _ in {1..30}; do
  if docker exec "$container_id" pg_isready -U qa -d qa >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
docker exec "$container_id" pg_isready -U qa -d qa >/dev/null

host_port=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')
export ASPNETCORE_ENVIRONMENT=Demo
export ASPNETCORE_URLS="http://127.0.0.1:$host_port"
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$db_port;Database=qa;Username=qa;Password=qa-local-test"
export GitHub__Repository='example/qa-agent-demo'
export GitHub__Branch='main'
export GitHub__SourcePath="$repository"
export Agent__WorkspaceRoot="$temporary/worktrees"
export AgentApi__Key='qa-local-test-key'
dotnet run --project "$root/src/QaAgent.Host/QaAgent.Host.csproj" \
  > "$temporary/host.log" 2>&1 &
host_pid=$!

for _ in {1..30}; do
  if curl -fsS "http://127.0.0.1:$host_port/healthz" > /dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$host_pid" 2>/dev/null; then
    cat "$temporary/host.log" >&2
    exit 1
  fi
  sleep 1
done
curl -fsS "http://127.0.0.1:$host_port/healthz" > /dev/null

payload=$(python3 -c 'import json,sys; print(json.dumps({"repository":"example/qa-agent-demo","branch":"main","baseSha":sys.argv[1],"headSha":sys.argv[2],"profileId":"demo"}))' "$base_sha" "$head_sha")
first=$(curl -fsS -H "X-Agent-Key: $AgentApi__Key" -H 'Content-Type: application/json' \
  --data-binary "$payload" "http://127.0.0.1:$host_port/api/runs")
second=$(curl -fsS -H "X-Agent-Key: $AgentApi__Key" -H 'Content-Type: application/json' \
  --data-binary "$payload" "http://127.0.0.1:$host_port/api/runs")
run_id=$(python3 -c 'import json,sys; a=json.loads(sys.argv[1]); b=json.loads(sys.argv[2]); assert a["runId"] == b["runId"] and not a["duplicate"] and b["duplicate"]; print(a["runId"])' "$first" "$second")

unauthorized_status=$(curl -sS -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' \
  --data-binary "$payload" "http://127.0.0.1:$host_port/api/runs")
[[ "$unauthorized_status" == '401' ]]
zero_payload=$(python3 -c 'import json,sys; p=json.loads(sys.argv[1]); p["baseSha"]="0"*40; print(json.dumps(p))' "$payload")
zero_status=$(curl -sS -o /dev/null -w '%{http_code}' -H "X-Agent-Key: $AgentApi__Key" \
  -H 'Content-Type: application/json' --data-binary "$zero_payload" \
  "http://127.0.0.1:$host_port/api/runs")
[[ "$zero_status" == '400' ]]

parallel_payload=$(python3 -c 'import json,sys; p=json.loads(sys.argv[1]); p["baseSha"]=sys.argv[2]; p["headSha"]=sys.argv[3]; print(json.dumps(p))' "$payload" "$head_sha" "$later_sha")
curl -fsS -H "X-Agent-Key: $AgentApi__Key" -H 'Content-Type: application/json' \
  --data-binary "$parallel_payload" "http://127.0.0.1:$host_port/api/runs" > "$temporary/parallel-one.json" &
parallel_one_pid=$!
curl -fsS -H "X-Agent-Key: $AgentApi__Key" -H 'Content-Type: application/json' \
  --data-binary "$parallel_payload" "http://127.0.0.1:$host_port/api/runs" > "$temporary/parallel-two.json" &
parallel_two_pid=$!
wait "$parallel_one_pid"
wait "$parallel_two_pid"
python3 -c 'import json,sys; a=json.load(open(sys.argv[1])); b=json.load(open(sys.argv[2])); assert a["runId"] == b["runId"]; assert sorted([a["duplicate"], b["duplicate"]]) == [False, True]' \
  "$temporary/parallel-one.json" "$temporary/parallel-two.json"

for _ in {1..30}; do
  result=$(curl -fsS -H "X-Agent-Key: $AgentApi__Key" \
    "http://127.0.0.1:$host_port/api/runs/$run_id")
  if python3 -c 'import json,sys; exit(0 if json.loads(sys.argv[1])["state"] == "Completed" else 1)' "$result"; then
    break
  fi
  sleep 1
done
python3 -c 'import json,sys; r=json.loads(sys.argv[1]); assert r["baseSha"] == sys.argv[2]; assert r["headSha"] == sys.argv[3]; assert r["finalStatus"] == "Blocked"; assert [f["path"] for f in r["changedFiles"]] == ["feature.txt"]; print("M1 PostgreSQL, API idempotency, worker and exact Git range: OK")' \
  "$result" "$base_sha" "$head_sha"
