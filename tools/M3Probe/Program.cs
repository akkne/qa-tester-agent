using System.Text.Json;
using QaAgent.Application.Execution;
using QaAgent.Domain;
using QaAgent.Infrastructure.Analysis;
using QaAgent.Infrastructure.Execution;
using QaAgent.Infrastructure.Git;

if (args.Length is not (4 or 5))
{
    Console.Error.WriteLine("Usage: M3Probe SOURCE BASE_SHA HEAD_SHA ARTIFACT_ROOT [CANDIDATE_TEST_FILE]");
    return 2;
}

string artifacts = Path.GetFullPath(args[3]);
Directory.CreateDirectory(artifacts);
GitCliWorkspace git = new(new GitWorkspaceOptions
{
    Source = args[0],
    WorkspaceRoot = Path.Combine(artifacts, "workspaces")
});
var snapshot = await git.PrepareAsync("demo", CommitSha.Parse(args[1]), CommitSha.Parse(args[2]),
    CancellationToken.None);
try
{
    if (args.Length == 5)
    {
        string candidate = Path.GetFullPath(args[4]);
        if (!File.Exists(candidate) || !candidate.EndsWith(".cs", StringComparison.Ordinal))
            throw new ArgumentException("Candidate test must be an existing C# file.");
        string target = Path.Combine(snapshot.WorkspacePath, "demo", "Demo.Tests", "M5CandidateTests.cs");
        if (File.Exists(target))
            throw new InvalidOperationException("Candidate test path already exists in headSha.");
        File.Copy(candidate, target);
    }
    DockerM3Executor executor = new(artifacts, new CoberturaCoverageCalculator());
    M3Result result = await executor.ExecuteAsync(snapshot, Guid.NewGuid(), CancellationToken.None);
    M3Outcome outcome = M3Outcome.From(result);
    Console.WriteLine(JsonSerializer.Serialize(new { Status = outcome.Status.ToString(), outcome.Reason, Result = result }));
    return outcome.Status == RunStatus.Passed ? 0 : 1;
}
finally
{
    Directory.Delete(snapshot.WorkspacePath, recursive: true);
}
