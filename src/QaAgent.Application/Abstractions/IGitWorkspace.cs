using QaAgent.Domain;

namespace QaAgent.Application.Abstractions;

public sealed record ChangedFile(string Path, string ChangeKind);

public sealed record GitSnapshot(
    string WorkspacePath,
    CommitSha BaseSha,
    CommitSha HeadSha,
    IReadOnlyList<ChangedFile> Files,
    string Diff);

public interface IGitWorkspace
{
    Task<GitSnapshot> PrepareAsync(
        string repositoryId,
        CommitSha baseSha,
        CommitSha headSha,
        CancellationToken cancellationToken);
}
