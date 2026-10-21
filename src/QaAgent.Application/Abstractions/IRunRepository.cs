using QaAgent.Domain;

namespace QaAgent.Application.Abstractions;

public interface IRunRepository
{
    Task<Run?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Run?> FindByPushAsync(
        string repositoryId,
        CommitSha baseSha,
        CommitSha headSha,
        int profileVersion,
        CancellationToken cancellationToken);

    Task AddAsync(Run run, CancellationToken cancellationToken);

    Task<Run?> FindOldestQueuedAsync(CancellationToken cancellationToken);

    Task<Run?> FindOldestRunningAsync(CancellationToken cancellationToken);

    Task UpdateAsync(Run run, CancellationToken cancellationToken);
}
