using QaAgent.Application.Abstractions;

namespace QaAgent.Application.Analysis;

public sealed record M2Context(string Text, IReadOnlyList<string> RequirementIds,
    IReadOnlyList<string> ChangedRequirementIds, IReadOnlyList<string> Gaps);

public interface IM2ContextReader
{
    Task<M2Context> ReadAsync(GitSnapshot snapshot, ProjectProfile profile, CancellationToken cancellationToken);
}

public sealed record TestBuildResult(bool Compiles, string Diagnostics);

public sealed record TestWritingRequest(
    GitSnapshot Snapshot,
    ProjectProfile Profile,
    IReadOnlyList<string> RequirementIds,
    bool SpecificationChanged,
    string Context,
    string PlanJson,
    string FindingsJson,
    Guid? RunId);

public sealed record TestWritingResult(
    IReadOnlyList<GeneratedTest> Tests,
    TestBuildResult Build,
    IReadOnlyList<string> Gaps,
    int InputTokens,
    int OutputTokens,
    int Fixes = 0);

/// <summary>
/// Writes tests in the repository's own language and framework and leaves them as new files in the workspace.
/// <see cref="TestBuildResult.Compiles"/> reports whether every written test compiled; <c>Fixes</c> counts failed
/// builds the writer repaired.
/// </summary>
public interface IM2TestWriter
{
    Task<TestWritingResult> WriteAsync(TestWritingRequest request, CancellationToken cancellationToken);
}
