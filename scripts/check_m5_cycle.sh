#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture_root="$(mktemp -d)"
artifact_root="${1:-/tmp/qa-agent-m5-cycle-evidence}"
mkdir -p "$artifact_root"
trap 'rm -rf "$fixture_root"' EXIT

git init --quiet --bare "$fixture_root/remote.git"
git clone --quiet --no-hardlinks "$repo_root" "$fixture_root/repo"
git -C "$fixture_root/repo" remote add fixture "$fixture_root/remote.git"
git -C "$fixture_root/repo" push --quiet fixture HEAD:refs/heads/main
base_sha="$(git -C "$fixture_root/repo" rev-parse HEAD)"

python3 - "$fixture_root/repo/demo/Demo.Web/Program.cs" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
source = path.read_text()
marker = 'app.MapGet("/cabinet/{id:guid}"'
check = '    if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) != id.ToString()) return Results.Forbid();'
start = source.index(marker)
position = source.index(check, start)
path.write_text(source[:position] + '    // M5 fixture: owner check accidentally removed.' + source[position + len(check):])
PY
git -C "$fixture_root/repo" add demo/Demo.Web/Program.cs
git -C "$fixture_root/repo" -c user.name='Demo Developer' -c user.email='developer@example.test' commit --quiet -m 'feat(demo): update cabinet route with access regression'
fault_sha="$(git -C "$fixture_root/repo" rev-parse HEAD)"
git -C "$fixture_root/repo" push --quiet fixture HEAD:refs/heads/main

cat > "$fixture_root/M5CandidateTests.cs" <<'CS'
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class M5CandidateTests
{
    [Fact]
    public async Task Cabinet_url_for_another_account_is_forbidden_and_does_not_reveal_email()
    {
        using DemoWebFactory factory = new();
        using HttpClient alice = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpClient bob = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string password = "secure-password-123";
        await alice.PostAsJsonAsync("/api/register", new RegistrationInput("alice@example.com", password));
        HttpResponseMessage registration = await bob.PostAsJsonAsync("/api/register", new RegistrationInput("bob@example.com", password));
        using JsonDocument account = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        Guid bobId = account.RootElement.GetProperty("id").GetGuid();
        await alice.PostAsJsonAsync("/api/login", new LoginInput("alice@example.com", password));

        HttpResponseMessage response = await alice.GetAsync($"/cabinet/{bobId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("bob@example.com", await response.Content.ReadAsStringAsync());
    }
}
CS

branch="qa/${fault_sha:0:12}-m5-local"
git -C "$fixture_root/repo" checkout --quiet -b "$branch"
cp "$fixture_root/M5CandidateTests.cs" "$fixture_root/repo/demo/Demo.Tests/M5CandidateTests.cs"
mkdir -p "$fixture_root/repo/revision"
printf '# M5 local fixture\n\nDEMO-REG-05: candidate test reproduces cabinet ownership regression.\n' > "$fixture_root/repo/revision/commit_${fault_sha:0:12}.md"
git -C "$fixture_root/repo" add demo/Demo.Tests/M5CandidateTests.cs "revision/commit_${fault_sha:0:12}.md"
git -C "$fixture_root/repo" -c user.name='QA Agent' -c user.email='qa-agent@example.test' commit --quiet -m 'test(qa): publish cabinet ownership regression'
qa_sha="$(git -C "$fixture_root/repo" rev-parse HEAD)"
git -C "$fixture_root/repo" push --quiet fixture "HEAD:refs/heads/$branch"

dotnet run --project "$repo_root/tools/M3Probe/M3Probe.csproj" -- "$fixture_root/repo" "$base_sha" "$fault_sha" "$artifact_root/first" "$fixture_root/M5CandidateTests.cs" > "$artifact_root/first.log" 2>&1 && {
    echo 'The faulty first update unexpectedly passed.' >&2
    exit 1
}

git -C "$fixture_root/repo" checkout --quiet main
cp "$fixture_root/M5CandidateTests.cs" "$fixture_root/repo/demo/Demo.Tests/M5CandidateTests.cs"
git -C "$fixture_root/repo" add demo/Demo.Tests/M5CandidateTests.cs
git -C "$fixture_root/repo" -c user.name='Demo Developer' -c user.email='developer@example.test' commit --quiet -m 'test(demo): accept cabinet ownership regression test'
accepted_sha="$(git -C "$fixture_root/repo" rev-parse HEAD)"

set +e
dotnet test "$fixture_root/repo/demo/Demo.Tests/Demo.Tests.csproj" --filter FullyQualifiedName~M5CandidateTests --nologo > "$artifact_root/accepted-before-fix.log" 2>&1
before_fix_status=$?
set -e
if [ "$before_fix_status" -eq 0 ]; then
    echo 'The accepted test unexpectedly passed before the fix.' >&2
    exit 1
fi

python3 - "$fixture_root/repo/demo/Demo.Web/Program.cs" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
source = path.read_text()
source = source.replace('    // M5 fixture: owner check accidentally removed.',
    '    if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) != id.ToString()) return Results.Forbid();', 1)
path.write_text(source)
PY
git -C "$fixture_root/repo" add demo/Demo.Web/Program.cs
git -C "$fixture_root/repo" -c user.name='Demo Developer' -c user.email='developer@example.test' commit --quiet -m 'fix(demo): restore cabinet ownership guard'
fix_sha="$(git -C "$fixture_root/repo" rev-parse HEAD)"
git -C "$fixture_root/repo" push --quiet fixture HEAD:refs/heads/main
dotnet run --project "$repo_root/tools/M3Probe/M3Probe.csproj" -- "$fixture_root/repo" "$accepted_sha" "$fix_sha" "$artifact_root/second" > "$artifact_root/second.log" 2>&1

python3 - "$artifact_root" "$base_sha" "$fault_sha" "$qa_sha" "$accepted_sha" "$fix_sha" "$branch" "$fixture_root/repo" <<'PY'
import json
from pathlib import Path
import subprocess
import sys

root = Path(sys.argv[1])
base, fault, qa, accepted, fix, branch, repo = sys.argv[2:]
first = json.loads((root / 'first.log').read_text().splitlines()[-1])
second = json.loads((root / 'second.log').read_text().splitlines()[-1])
first_browser = json.loads(first['Result']['BrowserJson'])
second_browser = json.loads(second['Result']['BrowserJson'])
assert first['Status'] == 'Failed' and first['Result']['FailedTests'] >= 1, first
assert first_browser['status'] == 'FAILED', first_browser
assert {item['name'] for item in first_browser['scenarios'] if item['status'] == 'FAILED'} == {'desktop', 'mobile'}, first_browser
assert second['Status'] == 'Passed' and second['Result']['FailedTests'] == 0, second
assert second_browser['status'] == 'PASSED', second_browser
assert {item['name'] for item in second_browser['scenarios'] if item['status'] == 'PASSED'} == {'desktop', 'mobile'}, second_browser
paths = subprocess.check_output(['git', '-C', repo, 'diff-tree', '--no-commit-id', '--name-only', '-r', qa], text=True).splitlines()
assert sorted(paths) == ['demo/Demo.Tests/M5CandidateTests.cs', f'revision/commit_{fault[:12]}.md'], paths
assert subprocess.check_output(['git', '-C', repo, 'rev-parse', f'{qa}^'], text=True).strip() == fault
assert subprocess.check_output(['git', '-C', repo, 'show', f'{accepted}:demo/Demo.Tests/M5CandidateTests.cs'])
assert subprocess.check_output(['git', '-C', repo, 'rev-parse', f'{fix}^'], text=True).strip() == accepted
summary = {
    'baseSha': base, 'firstHeadSha': fault, 'qaBranch': branch, 'qaCommit': qa,
    'acceptedTestSha': accepted, 'secondHeadSha': fix,
    'first': {'status': first['Status'], 'totalTests': first['Result']['TotalTests'], 'failedTests': first['Result']['FailedTests'], 'browser': first_browser['status']},
    'second': {'status': second['Status'], 'totalTests': second['Result']['TotalTests'], 'failedTests': second['Result']['FailedTests'], 'browser': second_browser['status']},
}
(root / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print(json.dumps(summary, indent=2))
PY
