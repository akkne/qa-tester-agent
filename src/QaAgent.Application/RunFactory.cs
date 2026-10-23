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
        int profileVersion,
        int attempt = 1) =>
        Run.Queue(id, repositoryId, baseSha, headSha, profileVersion, clock.UtcNow, attempt);
}
