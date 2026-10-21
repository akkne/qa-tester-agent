using System.Text.Json;
using QaAgent.Application.Abstractions;
using QaAgent.Domain;
using QaAgent.Infrastructure.Persistence;

namespace QaAgent.Host.Worker;

public sealed class RunWorker(IServiceScopeFactory scopeFactory, IClock clock, ILogger<RunWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedRunsAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Run worker cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task RecoverInterruptedRunsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        while (await unitOfWork.Runs.FindOldestRunningAsync(cancellationToken) is { } run)
        {
            run.Block("Worker restarted before the Git stage completed.", clock.UtcNow);
            await unitOfWork.Runs.UpdateAsync(run, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
    }

    private async Task ProcessOneAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        Run? run = await unitOfWork.Runs.FindOldestQueuedAsync(cancellationToken);
        if (run is null)
        {
            return;
        }

        run.Start(clock.UtcNow);
        await unitOfWork.Runs.UpdateAsync(run, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        try
        {
            IGitWorkspace git = scope.ServiceProvider.GetRequiredService<IGitWorkspace>();
            GitSnapshot snapshot = await git.PrepareAsync(
                run.RepositoryId, run.BaseSha, run.HeadSha, cancellationToken);
            RunDbContext db = scope.ServiceProvider.GetRequiredService<RunDbContext>();
            RunRecord record = await db.Runs.FindAsync([run.Id], cancellationToken)
                ?? throw new InvalidOperationException("Run disappeared from the database.");
            record.ChangedFilesJson = JsonSerializer.Serialize(snapshot.Files);
            record.Diff = snapshot.Diff;
            await db.SaveChangesAsync(cancellationToken);
            try
            {
                Directory.Delete(snapshot.WorkspacePath, recursive: true);
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Could not remove workspace for run {RunId}", run.Id);
            }

            run.Block("Git range captured. Analysis and execution start in M2 and M3.", clock.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Run {RunId} failed while preparing Git range", run.Id);
            run.Block($"Git range unavailable: {exception.Message}", clock.UtcNow);
        }

        await unitOfWork.Runs.UpdateAsync(run, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
