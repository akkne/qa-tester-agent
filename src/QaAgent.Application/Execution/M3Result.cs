using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;

namespace QaAgent.Application.Execution;

public sealed record StageCheckResult(bool Passed, int ExitCode, string LogPath, string Summary);

public enum M3ExecutionProfile { WebStand, LeadQualification }

public sealed record M3Result(
    string SourceSha,
    string ArtifactDirectory,
    IReadOnlyDictionary<string, StageCheckResult> Checks,
    int? TotalTests,
    int? FailedTests,
    CoverageResult? Coverage,
    bool StandAvailable,
    string? StandFailure,
    bool? BrowserPassed,
    string? BrowserJson,
    M3ExecutionProfile Profile = M3ExecutionProfile.WebStand)
{
    public bool TestsPassed => Checks.TryGetValue("test", out StageCheckResult? result) && result.Passed;
    public bool BuildPassed => Checks.TryGetValue("build", out StageCheckResult? result) && result.Passed;
    public bool AllChecksPassed => Checks.Values.All(result => result.Passed);
}

public interface IM3Executor
{
    Task<M3Result> ExecuteAsync(GitSnapshot snapshot, Guid runId, CancellationToken cancellationToken);
}
