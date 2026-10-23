using Microsoft.EntityFrameworkCore;
using QaAgent.Application.Abstractions;
using QaAgent.Domain;

namespace QaAgent.Infrastructure.Persistence;

public sealed class EfRunRepository(RunDbContext dbContext) : IRunRepository
{
    public async Task<Run?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        RunRecord? record = await dbContext.Runs.AsNoTracking()
            .SingleOrDefaultAsync(run => run.Id == id, cancellationToken);
        return record is null ? null : Restore(record);
    }

    public async Task<Run?> FindByPushAsync(
        string repositoryId,
        CommitSha baseSha,
        CommitSha headSha,
        int profileVersion,
        CancellationToken cancellationToken)
    {
        // A push may have several attempts after a blocked run; the latest one answers the push.
        RunRecord? record = await dbContext.Runs.AsNoTracking()
            .Where(run => run.RepositoryId == repositoryId && run.BaseSha == baseSha.Value &&
                run.HeadSha == headSha.Value && run.ProfileVersion == profileVersion)
            .OrderByDescending(run => run.Attempt)
            .FirstOrDefaultAsync(cancellationToken);
        return record is null ? null : Restore(record);
    }

    public async Task<Run?> FindOldestQueuedAsync(CancellationToken cancellationToken)
    {
        RunRecord? record = await dbContext.Runs.AsNoTracking()
            .Where(run => run.State == nameof(RunState.Queued))
            .OrderBy(run => run.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return record is null ? null : Restore(record);
    }

    public async Task<Run?> FindOldestRunningAsync(
        DateTimeOffset startedBefore,
        CancellationToken cancellationToken)
    {
        RunRecord? record = await dbContext.Runs.AsNoTracking()
            .Where(run => run.State == nameof(RunState.Running) && run.StartedAt <= startedBefore)
            .OrderBy(run => run.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return record is null ? null : Restore(record);
    }

    public Task AddAsync(Run run, CancellationToken cancellationToken) =>
        dbContext.Runs.AddAsync(ToRecord(run), cancellationToken).AsTask();

    public async Task<bool> TryStartAsync(
        Run run,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        run.Start(now);
        int updated = await dbContext.Runs
            .Where(record => record.Id == run.Id && record.State == nameof(RunState.Queued))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(record => record.State, nameof(RunState.Running))
                .SetProperty(record => record.StartedAt, now), cancellationToken);
        return updated == 1;
    }

    public async Task UpdateAsync(Run run, CancellationToken cancellationToken)
    {
        RunRecord record = await dbContext.Runs.SingleAsync(item => item.Id == run.Id, cancellationToken);
        record.State = run.State.ToString();
        record.FinalStatus = run.FinalStatus?.ToString();
        record.StartedAt = run.StartedAt;
        record.FinishedAt = run.FinishedAt;
        record.FailureReason = run.FailureReason;
    }

    private static RunRecord ToRecord(Run run) => new()
    {
        Id = run.Id,
        RepositoryId = run.RepositoryId,
        BaseSha = run.BaseSha.Value,
        HeadSha = run.HeadSha.Value,
        ProfileVersion = run.ProfileVersion,
        Attempt = run.Attempt,
        State = run.State.ToString(),
        FinalStatus = run.FinalStatus?.ToString(),
        CreatedAt = run.CreatedAt,
        StartedAt = run.StartedAt,
        FinishedAt = run.FinishedAt,
        FailureReason = run.FailureReason
    };

    private static Run Restore(RunRecord record) => Run.Restore(
        record.Id,
        record.RepositoryId,
        CommitSha.Parse(record.BaseSha),
        CommitSha.Parse(record.HeadSha),
        record.ProfileVersion,
        record.CreatedAt,
        record.StartedAt,
        record.FinishedAt,
        Enum.TryParse(record.FinalStatus, out RunStatus status) ? status : null,
        record.FailureReason,
        record.Attempt);
}
