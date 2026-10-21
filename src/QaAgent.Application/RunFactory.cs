using QaAgent.Application.Abstractions;
using QaAgent.Domain;

namespace QaAgent.Application;

public sealed class RunFactory(IClock clock)
{
    public Run Create(
        Guid id,
        string repositoryId,
        CommitSha baseSha,
        CommitSha headSha,
        int profileVersion) =>
        Run.Queue(id, repositoryId, baseSha, headSha, profileVersion, clock.UtcNow);
}
