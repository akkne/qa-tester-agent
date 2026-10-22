using System.Text.Json;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
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

        bool gitPrepared = false;
        try
        {
            using CancellationTokenSource stageTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stageTimeout.CancelAfter(TimeSpan.FromMinutes(
                configuration.GetValue<int>("Agent:StageTimeoutMinutes")));
            IGitWorkspace git = scope.ServiceProvider.GetRequiredService<IGitWorkspace>();
            GitSnapshot snapshot = await git.PrepareAsync(
                run.RepositoryId, run.BaseSha, run.HeadSha, stageTimeout.Token);
            gitPrepared = true;
            RunDbContext db = scope.ServiceProvider.GetRequiredService<RunDbContext>();
            RunRecord record = await db.Runs.FindAsync([run.Id], cancellationToken)
                ?? throw new InvalidOperationException("Run disappeared from the database.");
            record.ChangedFilesJson = JsonSerializer.Serialize(snapshot.Files);
            record.Diff = snapshot.Diff;
            await db.SaveChangesAsync(cancellationToken);
            try
            {
                M2Processor? m2 = scope.ServiceProvider.GetService<M2Processor>();
                if (m2 is null)
                {
                    run.Block("M2 analysis requires Llm:Model, Llm:BaseUrl and an available LLM service.", clock.UtcNow);
                }
                else
                {
                    const string profileId = "demo";
                    IConfigurationSection section = configuration.GetSection($"Profiles:{profileId}");
                    if (section.GetValue<int>("ProfileVersion") != run.ProfileVersion)
                    {
                        throw new InvalidOperationException("Stored profile version differs from the current demo profile.");
                    }
                    ProjectProfile profile = new(
                        profileId,
                        run.ProfileVersion,
                        section["SpecificationDirectory"] ?? throw new InvalidOperationException("SpecificationDirectory is missing."),
                        section.GetSection("AllowedTestDirectories").Get<string[]>() ?? [],
                        section["AllowedReportDirectory"] ?? "revision");
                    M2Result result = await m2.ProcessAsync(snapshot, profile, stageTimeout.Token);
                    record.M2ResultJson = JsonSerializer.Serialize(result);
                    await db.SaveChangesAsync(cancellationToken);
                    run.Block(result.TestsCompile
                        ? "M2 analysis and test generation complete; M3 execution is not implemented."
                        : "M2 generated tests did not compile after two fixes: " + result.BuildDiagnostics,
                        clock.UtcNow);
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(snapshot.WorkspacePath, recursive: true);
                }
                catch (IOException exception)
                {
                    logger.LogWarning(exception, "Could not remove workspace for run {RunId}", run.Id);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            run.Block(gitPrepared ? "M2 analysis timed out." : "Git stage timed out.", clock.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Run {RunId} failed during Git or M2 analysis", run.Id);
            run.Block($"{(gitPrepared ? "M2 analysis" : "Git range")} unavailable: {exception.Message}", clock.UtcNow);
        }

        await unitOfWork.Runs.UpdateAsync(run, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
