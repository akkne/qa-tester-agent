using System.Text.Json;
using Microsoft.Extensions.Logging;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Infrastructure.Analysis;
using QaAgent.Infrastructure.Llm;

namespace QaAgent.Infrastructure.Execution;

public sealed class AgentExecutionOptions
{
    public string Image { get; init; } = "qa-agent/test-runner:1";
    public string CodexHome { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public string? ApiKey { get; init; }
    public string? Model { get; init; }
    public int TimeoutMinutes { get; init; } = 45;
    public int CommandTimeoutSeconds { get; init; } = 1800;
    public string Cpus { get; init; } = "4";
    public string Memory { get; init; } = "8g";
    public bool AllowDocker { get; init; }
    public IReadOnlyList<string> Hints { get; init; } = [];
}

/// <summary>
/// Lets a Codex agent discover and run every test suite of any stack inside a disposable container. Verdicts come
/// from the qa-run ledger, parsed JUnit/TRX reports and a tracked-file diff; the agent's JSON only names the evidence.
/// </summary>
public sealed class AgentM3Executor(
    string artifactRoot,
    AgentExecutionOptions options,
    ICoverageCalculator coverageCalculator,
    ICodexActivityStore activity,
    ILogger<AgentM3Executor> logger) : IM3Executor
{
    private readonly CodexContainer codex = new(options, activity, logger);
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);
    // The container can write the repository; host git must not run hooks or an fsmonitor it planted.
    private static readonly string[] SafeGit = ["-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null"];

    public async Task<M3Result> ExecuteAsync(GitSnapshot snapshot, Guid runId, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(snapshot.WorkspacePath);
        string artifacts = Path.GetFullPath(Path.Combine(artifactRoot, runId.ToString("N")));
        Directory.CreateDirectory(artifacts);
        ProcessResult sha = await ProcessRunner.RunAsync("git", [.. SafeGit, "-C", root, "rev-parse", "HEAD"], null, cancellationToken);
        if (sha.ExitCode != 0 || !string.Equals(sha.Output.Trim(), snapshot.HeadSha.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The test workspace does not match headSha.");
        // Host-side M2 builds leave macOS output; the Linux container must start from clean sources.
        await ProcessRunner.RunAsync("git", [.. SafeGit, "-C", root, "clean", "-fdXq"], null, cancellationToken);

        // A repeated pass must never read evidence left by the previous one.
        string agentDirectory = Path.Combine(artifacts, "agent");
        if (Directory.Exists(agentDirectory)) Directory.Delete(agentDirectory, recursive: true);
        foreach (string directory in new[] { "commands", "reports", "coverage" })
            Directory.CreateDirectory(Path.Combine(agentDirectory, directory));

        // Tests written in M2 are untracked, so `git ls-files` in the container would not reveal them.
        ProcessResult untracked = await ProcessRunner.RunAsync("git",
            [.. SafeGit, "-C", root, "ls-files", "--others", "--exclude-standard", "-z"], null, cancellationToken);
        string[] addedFiles = untracked.ExitCode == 0
            ? untracked.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            : [];
        (string? answer, string? agentFailure, int inputTokens, int outputTokens) = await codex.RunAsync(
            root, agentDirectory,
            AgentTestContract.Prompt(options.Hints, options.AllowDocker, options.CommandTimeoutSeconds, addedFiles),
            AgentTestContract.Schema, new Dictionary<string, string>(), runId, "Execution", "Test agent", cancellationToken);

        List<string> warnings = [];
        AgentTestReport? report = null;
        if (answer is not null)
        {
            await CodexContainer.WriteOwnedAsync(Path.Combine(agentDirectory, "agent-report.json"), answer, cancellationToken);
            try
            {
                report = JsonSerializer.Deserialize<AgentTestReport>(answer, ReportJson);
                if (!IsComplete(report))
                {
                    report = null;
                    agentFailure = "The test agent report is missing required fields.";
                }
            }
            catch (JsonException error)
            {
                agentFailure = "The test agent returned invalid JSON: " + error.Message;
            }
        }

        IReadOnlyList<CommandRecord> ledger = TestReportParser.ReadLedger(
            Path.Combine(agentDirectory, "ledger.jsonl"), agentDirectory, warnings);
        Dictionary<string, IReadOnlyList<ParsedTestReport>> reports = new(StringComparer.Ordinal);
        foreach (string suite in ledger.Select(record => record.Suite)
                     .Concat(report?.Suites.Select(item => item.Id) ?? []).Distinct(StringComparer.Ordinal))
        {
            IEnumerable<string> declared = report?.Suites.Where(item => item.Id == suite)
                .SelectMany(item => item.ReportFiles) ?? [];
            reports[suite] = ReportFiles(agentDirectory, suite, declared)
                .Select(file => file.Path is null
                    ? new ParsedTestReport(file.Declared, 0, 0, 0, [], "path is outside /artifacts or missing")
                    : TestReportParser.Parse(file.Path))
                .ToArray();
        }

        // Diff against the expected commit, so a commit made inside the container cannot hide edits.
        ProcessResult head = await ProcessRunner.RunAsync("git", [.. SafeGit, "-C", root, "rev-parse", "HEAD"], null, cancellationToken);
        ProcessResult diff = await ProcessRunner.RunAsync("git",
            [.. SafeGit, "-C", root, "diff", "--name-only", snapshot.HeadSha.Value, "--"], null, cancellationToken);
        string[] modified = diff.ExitCode != 0
            ? ["(git diff failed: " + CodexContainer.Tail(diff.Output) + ")"]
            : !string.Equals(head.Output.Trim(), snapshot.HeadSha.Value, StringComparison.OrdinalIgnoreCase)
                ? ["(HEAD moved to " + CodexContainer.Tail(head.Output.Trim()) + ")"]
                : diff.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        AgentExecution agent = AgentExecution.Evaluate(report, ledger, reports, modified, inputTokens, outputTokens);
        agent = agent with
        {
            Warnings = [.. warnings, .. agent.Warnings],
            BlockReason = agentFailure is not null && report is null ? agentFailure : agent.BlockReason
        };

        Dictionary<string, StageCheckResult> checks = new(StringComparer.Ordinal);
        foreach (CommandRecord record in ledger.OrderBy(item => item.Sequence))
        {
            string summary = record.LogPath.Length > 0 && File.Exists(record.LogPath)
                ? await ReadTailAsync(record.LogPath, cancellationToken) : "";
            checks[record.Suite + "/" + record.Phase] = new StageCheckResult(record.ExitCode == 0, record.ExitCode, record.LogPath, summary);
        }

        string[] cobertura = (report?.Suites.SelectMany(item => item.CoverageFiles) ?? [])
            .Select(file => TestReportParser.MapArtifact(agentDirectory, file))
            .OfType<string>().Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray();
        CoverageResult? coverage = cobertura.Length == 0
            ? null
            : await coverageCalculator.CalculateAsync(snapshot, cobertura, cancellationToken);
        SuiteResult[] counted = agent.Suites.Where(suite => suite.CountsVerified).ToArray();
        CodexContainer.PruneEvidence(agentDirectory, "commands", "reports", "coverage", "ledger.jsonl",
            "agent-report.json", "container.log");

        return new M3Result(snapshot.HeadSha.Value, artifacts, checks,
            counted.Length == 0 ? null : counted.Sum(suite => suite.Total ?? 0),
            counted.Length == 0 ? null : counted.Sum(suite => suite.Failed ?? 0),
            coverage, false, null, null, null, M3ExecutionProfile.Agent, agent);
    }

    private static IEnumerable<(string Declared, string? Path)> ReportFiles(
        string agentDirectory, string suite, IEnumerable<string> declared)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string file in declared)
        {
            string? path = TestReportParser.MapArtifact(agentDirectory, file);
            if (path is not null && !File.Exists(path)) path = null;
            if (path is null || seen.Add(path)) yield return (file, path);
        }

        // Reports in the suite's own directory count even when the agent forgot to list them.
        string own = Path.Combine(agentDirectory, "reports", suite);
        if (TestReportParser.MapArtifact(agentDirectory, "reports/" + suite) is null || !Directory.Exists(own)) yield break;
        foreach (string path in Directory.EnumerateFiles(own, "*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith(".trx", StringComparison.OrdinalIgnoreCase)))
            if (!WorkspacePath.HasLink(agentDirectory, path) && seen.Add(path)) yield return (path, path);
    }

    // Logs can be gigabytes; only the tail is published.
    private static async Task<string> ReadTailAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        stream.Seek(Math.Max(0, stream.Length - 16_000), SeekOrigin.Begin);
        using StreamReader reader = new(stream);
        return CodexContainer.Tail(await reader.ReadToEndAsync(cancellationToken));
    }

    private static bool IsComplete(AgentTestReport? report) =>
        report is { Status: not null, Suites: not null, NotRun: not null } &&
        report.Suites.All(suite => suite is
        {
            Id: not null, Language: not null, Framework: not null, WorkingDirectory: not null,
            ReportFiles: not null, CoverageFiles: not null, FailureSummary: not null, Notes: not null
        }) &&
        report.NotRun.All(item => item is { Path: not null, Reason: not null });
}
