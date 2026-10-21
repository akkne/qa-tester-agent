namespace QaAgent.Application.Abstractions;

public sealed record CheckResult(bool Completed, int Passed, int Failed, string? FailureReason);

public interface ICheckExecutor
{
    Task<CheckResult> RunAllAsync(
        GitSnapshot snapshot,
        ProjectProfile profile,
        CancellationToken cancellationToken);
}
