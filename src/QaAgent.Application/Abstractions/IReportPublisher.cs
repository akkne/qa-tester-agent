using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Domain;

namespace QaAgent.Application.Abstractions;

public sealed record PublishedArtifact(string Id, string Name, string RelativePath, long Bytes);

public sealed record PublishedReport(
    string Branch,
    string ReportPath,
    string LocalReportPath,
    string? BranchUrl,
    string? ReportUrl,
    string? CommitSha,
    IReadOnlyList<PublishedArtifact> Artifacts,
    string? PublishError);

public interface IReportPublisher
{
    Task<PublishedReport> PublishAsync(
        Run run,
        GitSnapshot snapshot,
        ProjectProfile profile,
        M2Result analysis,
        M3Result execution,
        RunStatus status,
        CancellationToken cancellationToken);
}
