using System.Text.Json;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Domain;
using QaAgent.Infrastructure.Llm;
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
            -configuration.GetValue<int>("Agent:RunTimeoutMinutes") - 1);
        while (await unitOfWork.Runs.FindOldestRunningAsync(cutoff, cancellationToken) is { } run)
        {
            run.Block("Run did not finish before its timeout.", clock.UtcNow);
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

        ICodexActivityStore activity = scope.ServiceProvider.GetRequiredService<ICodexActivityStore>();
        async Task TraceAsync(string stage, string message)
        {
            try
            {
                await activity.AppendAsync(run.Id, stage, "status", message, CancellationToken.None);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(error, "Could not save activity for run {RunId}", run.Id);
            }
        }

        bool gitPrepared = false;
        bool m3Started = false;
        bool m4Started = false;
        try
        {
            await TraceAsync("Git", "Preparing the pushed commit and diff.");
            using CancellationTokenSource runTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runTimeout.CancelAfter(TimeSpan.FromMinutes(
                configuration.GetValue<int>("Agent:RunTimeoutMinutes")));
            using CancellationTokenSource gitTimeout = CancellationTokenSource.CreateLinkedTokenSource(runTimeout.Token);
            gitTimeout.CancelAfter(TimeSpan.FromMinutes(configuration.GetValue<int>("Agent:StageTimeoutMinutes")));
            IGitWorkspace git = scope.ServiceProvider.GetRequiredService<IGitWorkspace>();
            GitSnapshot snapshot = await git.PrepareAsync(
                run.RepositoryId, run.BaseSha, run.HeadSha, gitTimeout.Token);
            await TraceAsync("Git", $"Prepared {snapshot.Files.Count} changed files.");
            gitPrepared = true;
            RunDbContext db = scope.ServiceProvider.GetRequiredService<RunDbContext>();
            RunRecord record = await db.Runs.FindAsync([run.Id], cancellationToken)
                ?? throw new InvalidOperationException("Run disappeared from the database.");
            record.ChangedFilesJson = JsonSerializer.Serialize(snapshot.Files);
            record.Diff = snapshot.Diff;
            await db.SaveChangesAsync(cancellationToken);
            try
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

                // LLM analysis only adds candidate tests. When it fails, the run still checks the pushed code
                // with the repository's own tests instead of ending as Blocked.
                M2Result result;
                M2Processor? m2 = scope.ServiceProvider.GetService<M2Processor>();
                using CancellationTokenSource analysisTimeout = CancellationTokenSource.CreateLinkedTokenSource(runTimeout.Token);
                analysisTimeout.CancelAfter(TimeSpan.FromMinutes(configuration.GetValue("Agent:AnalysisTimeoutMinutes", 20)));
                try
                {
                    if (m2 is null)
                    {
                        throw new InvalidOperationException("M2 analysis requires an available Codex CLI LLM service.");
                    }

                    await TraceAsync("Analysis", "Reading the specification and preparing Codex requests.");
                    result = await m2.ProcessAsync(snapshot, profile, analysisTimeout.Token, run.Id);
                    await TraceAsync("Analysis", $"Generated {result.Tests.Count} tests; compilation {(result.TestsCompile ? "passed" : "failed")}.");
                }
                catch (Exception error) when (error is not SecretsDetectedException && !runTimeout.IsCancellationRequested)
                {
                    string reason = error is OperationCanceledException ? "LLM analysis timed out." : error.Message;
                    logger.LogWarning(error, "M2 analysis for run {RunId} was skipped", run.Id);
                    await TraceAsync("Analysis", "LLM analysis skipped; continuing with the repository's own tests: " + reason);
                    result = WithoutGeneratedTests(EmptyAnalysis, "LLM analysis skipped: " + reason);
                    await git.ResetAsync(snapshot, runTimeout.Token);
                }

                if (!result.TestsCompile)
                {
                    await TraceAsync("Analysis", "Generated tests did not compile; they are excluded from execution.");
                    result = WithoutGeneratedTests(result,
                        "Generated tests were excluded because they did not compile: " + Shorten(result.BuildDiagnostics));
                    await git.ResetAsync(snapshot, runTimeout.Token);
                }

                record.M2ResultJson = JsonSerializer.Serialize(result);
                await db.SaveChangesAsync(cancellationToken);

                m3Started = true;
                await TraceAsync("Execution", "Running build, tests and browser checks.");
                IM3Executor executor = scope.ServiceProvider.GetRequiredService<IM3Executor>();
                M3Result executed = await executor.ExecuteAsync(snapshot, run.Id, runTimeout.Token);
                // Agent checks are keyed by suite; a failed suite build reaches the Failed branch below instead.
                if (executed.Profile != M3ExecutionProfile.Agent && !executed.BuildPassed && result.Tests.Count > 0)
                {
                    // The Linux build applies the product's analyzers; a generated test must not fail the product.
                    string buildSummary = executed.Checks.TryGetValue("build", out StageCheckResult? build) ? build.Summary : "";
                    await TraceAsync("Execution", "Build failed with generated tests; repeating checks without them.");
                    result = WithoutGeneratedTests(result,
                        "Generated tests were excluded because the build failed with them: " + Shorten(buildSummary));
                    record.M2ResultJson = JsonSerializer.Serialize(result);
                    await db.SaveChangesAsync(cancellationToken);
                    await git.ResetAsync(snapshot, runTimeout.Token);
                    executed = await executor.ExecuteAsync(snapshot, run.Id, runTimeout.Token);
                }
                M3Outcome outcome = M3Outcome.From(executed);
                if (outcome.Status == RunStatus.Failed && result.Tests.Count > 0)
                {
                    // Generated tests are hypotheses. Check the product without them so that a test that only
                    // fails in the container (or encodes a wrong guess) cannot fail the push on its own.
                    await TraceAsync("Execution", "Checks failed with generated tests; repeating checks without them.");
                    string generatedFailure = executed.Checks.TryGetValue("test", out StageCheckResult? failedTest)
                        ? failedTest.Summary
                        : string.Join("; ", executed.Agent?.Suites.Where(suite => suite.Verdict == SuiteVerdict.Failed)
                            .Select(suite => $"{suite.Id}: {suite.Reason} {string.Join(", ", suite.FailedTests.Take(10))}") ?? []);
                    string generatedLog = Path.Combine(executed.ArtifactDirectory, "generated-tests-test.log");
                    if (failedTest is not null && File.Exists(failedTest.LogPath))
                        File.Copy(failedTest.LogPath, generatedLog, overwrite: true);
                    await git.ResetAsync(snapshot, runTimeout.Token);
                    M3Result baseline = await executor.ExecuteAsync(snapshot, run.Id, runTimeout.Token);
                    M3Outcome baselineOutcome = M3Outcome.From(baseline);
                    if (baselineOutcome.Status == RunStatus.Passed)
                    {
                        result = result with
                        {
                            ContextGaps = [.. result.ContextGaps,
                                $"Repository checks pass without the generated tests; {executed.FailedTests?.ToString() ?? "some"} " +
                                "generated tests failed and need review on the QA branch: " + Shorten(generatedFailure)]
                        };
                        executed = baseline;
                        outcome = new M3Outcome(RunStatus.NeedsReview, null);
                    }
                    else
                    {
                        result = result with
                        {
                            ContextGaps = [.. result.ContextGaps,
                                "Repository checks also fail without the generated tests."]
                        };
                        executed = baseline;
                        outcome = baselineOutcome;
                    }

                    // The QA branch keeps the generated tests for review, so put them back for publication.
                    foreach (GeneratedTest test in result.Tests)
                    {
                        string path = Path.Combine(snapshot.WorkspacePath, test.Path.Replace('\\', '/'));
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllTextAsync(path, test.Source, runTimeout.Token);
                    }
                    record.M2ResultJson = JsonSerializer.Serialize(result);
                }
                await TraceAsync("Execution", $"Tests: {executed.TotalTests?.ToString() ?? "unavailable"} total, {executed.FailedTests?.ToString() ?? "unavailable"} failed.");
                record.M3ResultJson = JsonSerializer.Serialize(executed);
                await db.SaveChangesAsync(cancellationToken);
                m4Started = true;
                await TraceAsync("Publication", "Publishing the QA branch and report.");
                IReportPublisher publisher = scope.ServiceProvider.GetRequiredService<IReportPublisher>();
                PublishedReport publication = await publisher.PublishAsync(
                    run, snapshot, profile, result, executed, outcome.Status, runTimeout.Token);
                record.M4ResultJson = JsonSerializer.Serialize(publication);
                await db.SaveChangesAsync(cancellationToken);
                if (publication.PublishError is not null)
                    run.Block(publication.PublishError, clock.UtcNow);
                else if (outcome.Status == RunStatus.Blocked)
                    run.Block(outcome.Reason!, clock.UtcNow);
                else
                    run.Complete(outcome.Status, clock.UtcNow);
            }
            finally
            {
                try
                {
                    Directory.Delete(snapshot.WorkspacePath, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "Could not remove workspace for run {RunId}", run.Id);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (run.State == RunState.Running) run.Block(m4Started ? "M4 publication timed out." : m3Started ? "M3 execution timed out." : gitPrepared ? "M2 analysis timed out." : "Git stage timed out.", clock.UtcNow);
            await TraceAsync("Run", run.FailureReason ?? "Run timed out.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Run {RunId} failed during Git, M2, M3, or M4", run.Id);
            if (run.State == RunState.Running) run.Block($"{(m4Started ? "M4 publication" : m3Started ? "M3 execution" : gitPrepared ? "M2 analysis" : "Git range")} unavailable: {exception.Message}", clock.UtcNow);
            await TraceAsync("Run", run.FailureReason ?? "Run failed.");
        }

        await unitOfWork.Runs.UpdateAsync(run, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        await TraceAsync("Run", $"Run finished: {run.FinalStatus?.ToString() ?? run.State.ToString()}.");
    }

    private static readonly M2Result EmptyAnalysis = new([], [], [], [], [], 0, 0, 0, true, null, []);

    private static M2Result WithoutGeneratedTests(M2Result result, string warning) => result with
    {
        Tests = [],
        TestsCompile = true,
        ContextGaps = [.. result.ContextGaps, warning]
    };

    private static string Shorten(string? text)
    {
        string value = (text ?? "").Trim();
        return value.Length <= 1500 ? value : "…" + value[^1500..];
    }
}
