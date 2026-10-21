using System.Text.Json;
using QaAgent.Application.Abstractions;
using QaAgent.Domain;
using QaAgent.Infrastructure.Persistence;

namespace QaAgent.Host.Worker;

public sealed class RunWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IConfiguration configuration,
    ILogger<RunWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverInterruptedRunsAsync(stoppingToken);
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
        DateTimeOffset cutoff = clock.UtcNow.AddMinutes(
            -configuration.GetValue<int>("Agent:StageTimeoutMinutes") - 1);
        while (await unitOfWork.Runs.FindOldestRunningAsync(cutoff, cancellationToken) is { } run)
        {
            run.Block("Git stage did not finish before its timeout.", clock.UtcNow);
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

        if (!await unitOfWork.Runs.TryStartAsync(run, clock.UtcNow, cancellationToken))
        {
            return;
        }

        try
        {
            using CancellationTokenSource stageTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stageTimeout.CancelAfter(TimeSpan.FromMinutes(
                configuration.GetValue<int>("Agent:StageTimeoutMinutes")));
            IGitWorkspace git = scope.ServiceProvider.GetRequiredService<IGitWorkspace>();
            GitSnapshot snapshot = await git.PrepareAsync(
                run.RepositoryId, run.BaseSha, run.HeadSha, stageTimeout.Token);
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            run.Block("Git stage timed out.", clock.UtcNow);
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
