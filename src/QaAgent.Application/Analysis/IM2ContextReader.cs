using QaAgent.Application.Abstractions;

namespace QaAgent.Application.Analysis;

public sealed record M2Context(string Text, IReadOnlyList<string> RequirementIds,
    IReadOnlyList<string> ChangedRequirementIds, IReadOnlyList<string> Gaps);

public interface IM2ContextReader
{
    Task<M2Context> ReadAsync(GitSnapshot snapshot, ProjectProfile profile, CancellationToken cancellationToken);
}

public sealed record TestBuildResult(bool Compiles, string Diagnostics);

public interface IM2TestBuilder
{
    Task<TestBuildResult> BuildAsync(
        GitSnapshot snapshot,
        ProjectProfile profile,
        IReadOnlyList<GeneratedTest> tests,
        CancellationToken cancellationToken);

    void Cleanup();
}
