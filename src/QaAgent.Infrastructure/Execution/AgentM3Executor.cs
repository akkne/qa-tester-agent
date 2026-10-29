using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
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
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);

    public async Task<M3Result> ExecuteAsync(GitSnapshot snapshot, Guid runId, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(snapshot.WorkspacePath);
        string artifacts = Path.GetFullPath(Path.Combine(artifactRoot, runId.ToString("N")));
        Directory.CreateDirectory(artifacts);
        ProcessResult sha = await RunAsync("git", ["-C", root, "rev-parse", "HEAD"], null, cancellationToken);
        if (sha.ExitCode != 0 || !string.Equals(sha.Output.Trim(), snapshot.HeadSha.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The test workspace does not match headSha.");
        // Host-side M2 builds leave macOS output; the Linux container must start from clean sources.
        await RunAsync("git", ["-C", root, "clean", "-fdXq"], null, cancellationToken);

        // A repeated pass must never read evidence left by the previous one.
        string agentDirectory = Path.Combine(artifacts, "agent");
        if (Directory.Exists(agentDirectory)) Directory.Delete(agentDirectory, recursive: true);
        foreach (string directory in new[] { "commands", "reports", "coverage" })
            Directory.CreateDirectory(Path.Combine(agentDirectory, directory));

        (string? answer, string? agentFailure, int inputTokens, int outputTokens) =
            await RunAgentAsync(root, agentDirectory, runId, cancellationToken);

        List<string> warnings = [];
        AgentTestReport? report = null;
        if (answer is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(agentDirectory, "agent-report.json"), answer, cancellationToken);
            try
            {
                report = JsonSerializer.Deserialize<AgentTestReport>(answer, ReportJson);
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

        ProcessResult diff = await RunAsync("git", ["-C", root, "diff", "--name-only", "HEAD"], null, cancellationToken);
        string[] modified = diff.ExitCode == 0
            ? diff.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["(git diff failed: " + Tail(diff.Output) + ")"];

        AgentExecution agent = AgentExecution.Evaluate(report, ledger, reports, modified, inputTokens, outputTokens);
        agent = agent with
        {
            Warnings = [.. warnings, .. agent.Warnings],
            BlockReason = agentFailure is not null && report is null ? agentFailure : agent.BlockReason
        };

        Dictionary<string, StageCheckResult> checks = new(StringComparer.Ordinal);
        foreach (CommandRecord record in ledger.OrderBy(item => item.Sequence))
        {
            string summary = File.Exists(record.LogPath) ? Tail(await File.ReadAllTextAsync(record.LogPath, cancellationToken)) : "";
            checks[record.Suite + "/" + record.Phase] = new StageCheckResult(record.ExitCode == 0, record.ExitCode, record.LogPath, summary);
        }

        string[] cobertura = (report?.Suites.SelectMany(item => item.CoverageFiles) ?? [])
            .Select(file => TestReportParser.MapArtifact(agentDirectory, file))
            .OfType<string>().Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray();
        CoverageResult? coverage = cobertura.Length == 0
            ? null
            : await coverageCalculator.CalculateAsync(snapshot, cobertura, cancellationToken);
        SuiteResult[] counted = agent.Suites.Where(suite => suite.CountsVerified).ToArray();

        return new M3Result(snapshot.HeadSha.Value, artifacts, checks,
            counted.Length == 0 ? null : counted.Sum(suite => suite.Total ?? 0),
            counted.Length == 0 ? null : counted.Sum(suite => suite.Failed ?? 0),
            coverage, false, null, null, null, M3ExecutionProfile.Agent, agent);
    }

    private async Task<(string? Answer, string? Failure, int InputTokens, int OutputTokens)> RunAgentAsync(
        string root, string agentDirectory, Guid runId, CancellationToken cancellationToken)
    {
        string control = Directory.CreateTempSubdirectory("qa-agent-test-").FullName;
        string container = $"qa-test-{runId.ToString("N")[..12]}-{Guid.NewGuid().ToString("N")[..6]}";
        string codexHome = Path.Combine(control, "codex-home");
        string answerPath = Path.Combine(control, "out", "answer.json");
        string authSource = Path.Combine(options.CodexHome, "auth.json");
        string authCopy = Path.Combine(codexHome, "auth.json");
        try
        {
            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(Path.GetDirectoryName(answerPath)!);
            await File.WriteAllTextAsync(Path.Combine(control, "schema.json"), AgentTestContract.Schema, cancellationToken);
            bool useApiKey = !string.IsNullOrWhiteSpace(options.ApiKey);
            if (!useApiKey)
            {
                if (!File.Exists(authSource))
                    return (null, "Codex login for the test agent is missing: " + authSource, 0, 0);
                File.Copy(authSource, authCopy);
            }

            List<string> arguments =
            [
                "run", "--rm", "-i", "--name", container,
                "--cpus", options.Cpus, "--memory", options.Memory, "--pids-limit", "4096",
                "--volume", $"{root}:{AgentTestContract.SourceMount}",
                "--volume", $"{agentDirectory}:{AgentTestContract.ArtifactsMount}",
                "--volume", $"{control}:/qa",
                "--workdir", AgentTestContract.SourceMount,
                "--env", "CODEX_HOME=/qa/codex-home",
                "--env", "QA_ARTIFACTS=" + AgentTestContract.ArtifactsMount,
                "--env", "QA_COMMAND_TIMEOUT=" + options.CommandTimeoutSeconds,
                "--env", "QA_DOCKER_AVAILABLE=" + (options.AllowDocker ? "1" : "0")
            ];
            if (useApiKey) arguments.AddRange(["--env", "CODEX_API_KEY"]);
            if (options.AllowDocker) arguments.AddRange(["--volume", "/var/run/docker.sock:/var/run/docker.sock"]);
            arguments.AddRange([options.Image, "codex", "exec", "--json", "--ephemeral", "--skip-git-repo-check",
                // The container is the sandbox: Codex's own sandbox cannot nest inside Docker.
                "--dangerously-bypass-approvals-and-sandbox", "--ignore-user-config",
                "-C", AgentTestContract.SourceMount, "--output-schema", "/qa/schema.json",
                "--output-last-message", "/qa/out/answer.json"]);
            if (!string.IsNullOrWhiteSpace(options.Model)) arguments.AddRange(["--model", options.Model]);
            arguments.Add("-");

            ProcessStartInfo start = new("docker")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            // The key travels by environment name only, never in the process arguments.
            if (useApiKey) start.Environment["CODEX_API_KEY"] = options.ApiKey;

            await TraceAsync(runId, "status", "Test agent started in " + options.Image + ".");
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start docker.");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(options.TimeoutMinutes));
            (int Input, int Output)? usage;
            string errors;
            try
            {
                Task<(int Input, int Output)?> events = CodexEventStream.ReadUsageAsync(process.StandardOutput,
                    line => RecordEventAsync(runId, line), timeout.Token);
                Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.StandardInput.WriteAsync(AgentTestContract.Prompt(options.Hints, options.AllowDocker,
                    options.CommandTimeoutSeconds).AsMemory(), timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                usage = await events;
                errors = await stderr;
            }
            catch (OperationCanceledException)
            {
                await RunAsync("docker", ["rm", "--force", container], null, CancellationToken.None);
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                if (cancellationToken.IsCancellationRequested) throw;
                await TraceAsync(runId, "status", $"Test agent exceeded {options.TimeoutMinutes} minutes.");
                return (null, $"The test agent exceeded {options.TimeoutMinutes} minutes.", 0, 0);
            }

            await File.WriteAllTextAsync(Path.Combine(agentDirectory, "container.log"), Tail(errors), cancellationToken);
            await RestoreRotatedLoginAsync(useApiKey, authSource, authCopy, cancellationToken);
            (int input, int output) = usage ?? (0, 0);
            await TraceAsync(runId, "status", $"Test agent finished with exit {process.ExitCode}. Tokens: {input} in, {output} out.");
            if (process.ExitCode != 0 || !File.Exists(answerPath))
                // Raw stderr stays in the local container.log; the reason is published with the report.
                return (null, $"The test agent exited {process.ExitCode} without a final report; see agent/container.log.",
                    input, output);
            string answer = await File.ReadAllTextAsync(answerPath, cancellationToken);
            return answer.Length > 512_000
                ? (null, "The test agent report exceeds 512 KB.", input, output)
                : (answer, null, input, output);
        }
        finally
        {
            Directory.Delete(control, recursive: true);
        }
    }

    // ChatGPT logins rotate refresh tokens; keep the host login valid when the container refreshed it.
    private async Task RestoreRotatedLoginAsync(bool useApiKey, string source, string copy, CancellationToken cancellationToken)
    {
        if (useApiKey || !File.Exists(copy)) return;
        string refreshed = await File.ReadAllTextAsync(copy, cancellationToken);
        if (refreshed == await File.ReadAllTextAsync(source, cancellationToken)) return;
        try
        {
            using JsonDocument _ = JsonDocument.Parse(refreshed);
            await File.WriteAllTextAsync(source, refreshed, cancellationToken);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "Could not restore the refreshed Codex login");
        }
    }

    private async Task RecordEventAsync(Guid runId, string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("type", out JsonElement type) && type.GetString() == "item.completed" &&
            root.TryGetProperty("item", out JsonElement item) &&
            item.TryGetProperty("type", out JsonElement itemType) && itemType.GetString() == "agent_message" &&
            item.TryGetProperty("text", out JsonElement text) && text.GetString() is { Length: > 0 } message)
            await TraceAsync(runId, "message", message);
    }

    private async Task TraceAsync(Guid runId, string kind, string text)
    {
        try
        {
            await activity.AppendAsync(runId, "Execution", kind, text, CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "Could not save test agent activity for run {RunId}", runId);
        }
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
            if (seen.Add(path)) yield return (path, path);
    }

    private static string Tail(string value) => value.Length <= 4000 ? value : value[^4000..];

    private static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        string? workingDirectory, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {executable}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }

        return new ProcessResult(process.ExitCode, await stdout + "\n" + await stderr);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
