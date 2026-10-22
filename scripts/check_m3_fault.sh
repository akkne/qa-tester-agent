#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture_root="$(mktemp -d)"
artifact_root="${1:-$repo_root/artifacts/m3-fault-evidence}"
mkdir -p "$artifact_root"
trap 'rm -rf "$fixture_root"' EXIT

git clone --quiet --no-hardlinks "$repo_root" "$fixture_root/repo"
python3 - "$fixture_root/repo/demo/Demo.Web/Program.cs" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
source = path.read_text()
marker = 'app.MapGet("/cabinet/{id:guid}"'
check = '    if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) != id.ToString()) return Results.Forbid();'
start = source.index(marker)
position = source.index(check, start)
path.write_text(source[:position] + '    // M3 fault fixture: owner check removed.' + source[position + len(check):])
PY
git -C "$fixture_root/repo" -c user.name='QA Fixture' -c user.email='fixture@example.test' add demo/Demo.Web/Program.cs
git -C "$fixture_root/repo" -c user.name='QA Fixture' -c user.email='fixture@example.test' commit --quiet -m 'fixture: remove cabinet owner check'
base_sha="$(git -C "$fixture_root/repo" rev-parse HEAD^)"
head_sha="$(git -C "$fixture_root/repo" rev-parse HEAD)"

set +e
dotnet run --project "$repo_root/tools/M3Probe/M3Probe.csproj" -- "$fixture_root/repo" "$base_sha" "$head_sha" "$artifact_root" > "$artifact_root/fault-result.log" 2>&1
probe_status=$?
set -e
python3 - "$artifact_root/fault-result.log" "$probe_status" <<'PY'
import json
from pathlib import Path
import sys

lines = Path(sys.argv[1]).read_text().splitlines()
result = json.loads(lines[-1])
execution = result['Result']
browser = json.loads(execution['BrowserJson'])
assert int(sys.argv[2]) == 1, 'Fault probe must exit nonzero'
assert result['Status'] == 'Failed', result
assert execution['FailedTests'] >= 1, result
assert browser['status'] == 'FAILED', browser
assert {scenario['name'] for scenario in browser['scenarios'] if scenario['status'] == 'FAILED'} == {'desktop', 'mobile'}, browser
for scenario in browser['scenarios']:
    for kind in ('screenshot', 'trace', 'diagnostics'):
        assert Path(scenario['artifacts'][kind]).is_file(), scenario
print(f"M3 fault reproduced: {execution['SourceSha']}, {execution['FailedTests']} failing tests, desktop/mobile screenshots and traces")
PY
