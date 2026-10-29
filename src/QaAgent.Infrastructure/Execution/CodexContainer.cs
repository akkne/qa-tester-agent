using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QaAgent.Application.Execution;
using QaAgent.Infrastructure.Analysis;
using QaAgent.Infrastructure.Llm;

namespace QaAgent.Infrastructure.Execution;

public sealed record CodexContainerResult(string? Answer, string? Failure, int InputTokens, int OutputTokens);

/// <summary>
/// Runs one `codex exec` agent inside a disposable test-runner container: the repository at /src, evidence at
/// /artifacts, the prompt's control files at /qa. Shared by the test-execution and the test-writing agents.
/// </summary>
public sealed class CodexContainer(AgentExecutionOptions options, ICodexActivityStore activity, ILogger logger)
{
    public async Task<CodexContainerResult> RunAsync(
        string root,
        string artifactsDirectory,
        string prompt,
        string schema,
        IReadOnlyDictionary<string, string> controlFiles,
        Guid runId,
        string stage,
        string agentName,
        CancellationToken cancellationToken)
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
            await File.WriteAllTextAsync(Path.Combine(control, "schema.json"), schema, cancellationToken);
            foreach ((string name, string content) in controlFiles)
                await File.WriteAllTextAsync(Path.Combine(control, name), content, cancellationToken);
            bool useApiKey = !string.IsNullOrWhiteSpace(options.ApiKey);
            if (!useApiKey)
            {
                if (!File.Exists(authSource))
                    return new(null, $"Codex login for the {agentName.ToLowerInvariant()} is missing: " + authSource, 0, 0);
                File.Copy(authSource, authCopy);
            }

            List<string> arguments =
            [
                "run", "--rm", "-i", "--name", container,
                "--cpus", options.Cpus, "--memory", options.Memory, "--pids-limit", "4096",
                "--volume", $"{root}:{AgentTestContract.SourceMount}",
                "--volume", $"{artifactsDirectory}:{AgentTestContract.ArtifactsMount}",
                "--volume", $"{control}:/qa",
                "--workdir", AgentTestContract.SourceMount,
                "--env", "CODEX_HOME=/qa/codex-home",
                "--env", "QA_ARTIFACTS=" + AgentTestContract.ArtifactsMount,
                "--env", "QA_COMMAND_TIMEOUT=" + options.CommandTimeoutSeconds,
                "--env", "QA_DOCKER_AVAILABLE=" + (options.AllowDocker ? "1" : "0")
            ];
            // Git metadata stays read-only: a planted hook or fsmonitor would otherwise run on the host.
            string git = Path.Combine(root, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                arguments.AddRange(["--volume", $"{git}:{AgentTestContract.SourceMount}/.git:ro"]);
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

            await TraceAsync(runId, stage, "status", $"{agentName} started in {options.Image}.");
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start docker.");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(options.TimeoutMinutes));
            (int Input, int Output)? usage;
            string errors;
            try
            {
                Task<(int Input, int Output)?> events = CodexEventStream.ReadUsageAsync(process.StandardOutput,
                    line => RecordEventAsync(runId, stage, line), timeout.Token);
                Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.StandardInput.WriteAsync(prompt.AsMemory(), timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                usage = await events;
                errors = await stderr;
            }
            catch (OperationCanceledException)
            {
                await ProcessRunner.RunAsync("docker", ["rm", "--force", container], null, CancellationToken.None);
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                if (cancellationToken.IsCancellationRequested) throw;
                await TraceAsync(runId, stage, "status", $"{agentName} exceeded {options.TimeoutMinutes} minutes.");
                return new(null, $"The {agentName.ToLowerInvariant()} exceeded {options.TimeoutMinutes} minutes.", 0, 0);
            }

            await WriteOwnedAsync(Path.Combine(artifactsDirectory, "container.log"), Tail(errors), cancellationToken);
            await RestoreRotatedLoginAsync(useApiKey, authSource, authCopy, cancellationToken);
            (int input, int output) = usage ?? (0, 0);
            await TraceAsync(runId, stage, "status",
                $"{agentName} finished with exit {process.ExitCode}. Tokens: {input} in, {output} out.");
            if (process.ExitCode != 0 || !File.Exists(answerPath) || WorkspacePath.HasLink(control, answerPath))
                // Raw stderr stays in the local container.log; the reason is published with the report.
                return new(null, $"The {agentName.ToLowerInvariant()} exited {process.ExitCode} without a final report; " +
                    "see container.log.", input, output);
            string answer = await File.ReadAllTextAsync(answerPath, cancellationToken);
            return answer.Length > 512_000
                ? new(null, $"The {agentName.ToLowerInvariant()} report exceeds 512 KB.", input, output)
                : new(answer, null, input, output);
        }
        finally
        {
            Directory.Delete(control, recursive: true);
        }
    }

    // The container may have planted a link at this path; replace it instead of writing through it.
    internal static Task WriteOwnedAsync(string path, string content, CancellationToken cancellationToken)
    {
        File.Delete(path);
        return File.WriteAllTextAsync(path, content, cancellationToken);
    }

    /// <summary>
    /// Deletes everything the agent left in its evidence directory except the named entries, so an SDK or cache
    /// installed there does not stay on the host. Links are removed, never followed.
    /// </summary>
    internal static void PruneEvidence(string directory, params string[] keep)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (keep.Contains(Path.GetFileName(entry), StringComparer.Ordinal)) continue;
            FileSystemInfo info = Directory.Exists(entry) ? new DirectoryInfo(entry) : new FileInfo(entry);
            if (info.LinkTarget is not null || info is FileInfo) info.Delete();
            else Directory.Delete(entry, recursive: true);
        }
    }

    internal static string Tail(string value) => value.Length <= 4000 ? value : value[^4000..];

    // ChatGPT logins rotate refresh tokens; keep the host login valid when the container refreshed it.
    private async Task RestoreRotatedLoginAsync(bool useApiKey, string source, string copy, CancellationToken cancellationToken)
    {
        // The container owns /qa: only a regular file at the original location may replace the host login.
        if (useApiKey || !File.Exists(copy) || WorkspacePath.HasLink(Path.GetDirectoryName(Path.GetDirectoryName(copy))!, copy))
            return;
        string refreshed = await File.ReadAllTextAsync(copy, cancellationToken);
        if (refreshed == await File.ReadAllTextAsync(source, cancellationToken)) return;
        try
        {
            using JsonDocument login = JsonDocument.Parse(refreshed);
            if (login.RootElement.ValueKind != JsonValueKind.Object || !login.RootElement.TryGetProperty("tokens", out _) &&
                !login.RootElement.TryGetProperty("OPENAI_API_KEY", out _))
                return;
            await File.WriteAllTextAsync(source, refreshed, cancellationToken);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "Could not restore the refreshed Codex login");
        }
    }

    private async Task RecordEventAsync(Guid runId, string stage, string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("type", out JsonElement type) && type.GetString() == "item.completed" &&
            root.TryGetProperty("item", out JsonElement item) &&
            item.TryGetProperty("type", out JsonElement itemType) && itemType.GetString() == "agent_message" &&
            item.TryGetProperty("text", out JsonElement text) && text.GetString() is { Length: > 0 } message)
            await TraceAsync(runId, stage, "message", message);
    }

    private async Task TraceAsync(Guid runId, string stage, string kind, string text)
    {
        try
        {
            await activity.AppendAsync(runId, stage, kind, text, CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "Could not save {Stage} agent activity for run {RunId}", stage, runId);
        }
    }
}

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
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

        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Output => StandardOutput + "\n" + StandardError;
}
