namespace QaAgent.Application.Abstractions;

public sealed record TestCandidate(string Path, IReadOnlyList<string> RequirementIds);

public interface ITestGenerator
{
    Task<IReadOnlyList<TestCandidate>> GenerateAsync(
        GitSnapshot snapshot,
        ProjectProfile profile,
        IReadOnlyList<Requirement> requirements,
        CancellationToken cancellationToken);
}
