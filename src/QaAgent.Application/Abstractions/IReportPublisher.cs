using QaAgent.Domain;

namespace QaAgent.Application.Abstractions;

public sealed record PublishedReport(string Branch, string ReportPath, Uri BranchUrl);

public interface IReportPublisher
{
    Task<PublishedReport> PublishAsync(
        Run run,
        GitSnapshot snapshot,
        string markdown,
        IReadOnlyList<TestCandidate> tests,
        CancellationToken cancellationToken);
}
