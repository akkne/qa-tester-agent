using System.Diagnostics;
using System.Text.Json;

namespace QaAgent.Infrastructure.Llm;

public sealed record CodexExecResult(string Json, int InputTokens, int OutputTokens);

public interface ICodexExecRunner
{
    Task<CodexExecResult> RunAsync(string prompt, string jsonSchema, CodexExecOptions options,
        CancellationToken cancellationToken);
}

public sealed class CodexExecRunner : ICodexExecRunner
{
    public async Task<CodexExecResult> RunAsync(string prompt, string jsonSchema, CodexExecOptions options,
        CancellationToken cancellationToken)
    {
        string runDirectory = Directory.CreateTempSubdirectory("qa-agent-codex-").FullName;
        string schemaPath = Path.Combine(runDirectory, "schema.json");
        string answerPath = Path.Combine(runDirectory, "answer.json");
        try
        {
            await File.WriteAllTextAsync(schemaPath, jsonSchema, cancellationToken);
            ProcessStartInfo start = new()
            {
                FileName = options.Executable,
                WorkingDirectory = runDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // The host may hold GitHub and API credentials; Codex only needs its own login and network setup.
            start.Environment.Clear();
            foreach (string name in new[]
            {
                "PATH", "HOME", "USER", "LOGNAME", "TMPDIR", "CODEX_HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME",
                "LANG", "LC_ALL", "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "SSL_CERT_FILE", "SSL_CERT_DIR"
            })
            {
                string? value = Environment.GetEnvironmentVariable(name);
                if (value is not null) start.Environment[name] = value;
            }
            foreach (string argument in new[]
            {
                "exec", "--json", "--ephemeral", "--skip-git-repo-check", "--sandbox", "read-only",
                "--ignore-user-config", "-C", runDirectory, "--output-schema", schemaPath,
                "--output-last-message", answerPath
            })
            {
                start.ArgumentList.Add(argument);
            }

            if (!string.IsNullOrWhiteSpace(options.Model))
            {
                start.ArgumentList.Add("--model");
                start.ArgumentList.Add(options.Model);
            }

            start.ArgumentList.Add("-");
            using Process process = new() { StartInfo = start };
            try
            {
                process.Start();
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                throw new InvalidOperationException("Codex CLI executable was not found or could not start.", error);
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            try
            {
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.StandardInput.WriteAsync(prompt.AsMemory(), timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                string events = await stdout;
                string errors = await stderr;
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(DescribeFailure(errors, process.ExitCode));
                }

                (int input, int output) = ParseUsage(events);
                if (!File.Exists(answerPath))
                {
                    throw new InvalidDataException("Codex completed without a final response file.");
                }

                return new CodexExecResult(await File.ReadAllTextAsync(answerPath, timeout.Token), input, output);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Codex LLM call exceeded {options.TimeoutSeconds} seconds.");
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            Directory.Delete(runDirectory, recursive: true);
        }
    }

    private static (int Input, int Output) ParseUsage(string jsonl)
    {
        foreach (string line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("type", out JsonElement type) && type.GetString() == "turn.completed" &&
                root.TryGetProperty("usage", out JsonElement usage) &&
                usage.TryGetProperty("input_tokens", out JsonElement input) && input.TryGetInt32(out int inputTokens) &&
                usage.TryGetProperty("output_tokens", out JsonElement output) && output.TryGetInt32(out int outputTokens) &&
                inputTokens >= 0 && outputTokens >= 0)
            {
                return (inputTokens, outputTokens);
            }
        }

        throw new InvalidDataException("Codex did not report completed token usage.");
    }

    private static string DescribeFailure(string stderr, int exitCode)
    {
        if (stderr.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("usage limit", StringComparison.OrdinalIgnoreCase))
        {
            return "Codex usage or rate limit reached.";
        }

        if (stderr.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "Codex authentication is unavailable; run `codex login` for the service account.";
        }

        return $"Codex CLI exited with code {exitCode}.";
    }
}
