using System.Diagnostics;
using System.Text.RegularExpressions;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Execution;

namespace QaAgent.Infrastructure.Execution;

/// <summary>Runs the bot's standalone xUnit v3 runner in an isolated .NET SDK container.</summary>
public sealed partial class LeadQualificationM3Executor(string artifactRoot) : IM3Executor
{
    private const string CheckScript = """
        run_check() {
          name="$1"; shift
          "$@" >"/artifacts/$name.log" 2>&1
          printf '%s' "$?" >"/artifacts/$name.exit"
        }
        run_check restore dotnet restore LeadQualification.slnx --nologo
        run_check format-whitespace dotnet format whitespace LeadQualification.slnx --verify-no-changes --no-restore
        run_check format-style dotnet format style LeadQualification.slnx --verify-no-changes --no-restore
        run_check build dotnet build LeadQualification.slnx --no-restore --nologo -warnaserror
        run_check test dotnet run --project tests/LeadQualification.Tests/LeadQualification.Tests.csproj --no-build --no-restore
        """;

    public async Task<M3Result> ExecuteAsync(GitSnapshot snapshot, Guid runId, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(snapshot.WorkspacePath);
        string artifacts = Path.GetFullPath(Path.Combine(artifactRoot, runId.ToString("N")));
        Directory.CreateDirectory(artifacts);
        ProcessResult sha = await RunAsync("git", ["-C", root, "rev-parse", "HEAD"], root,
            Path.Combine(artifacts, "source-sha.log"), cancellationToken);
        if (sha.ExitCode != 0 || !string.Equals(sha.Output.Trim(), snapshot.HeadSha.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The test workspace does not match headSha.");

        ProcessResult container = await RunAsync("docker",
            ["run", "--rm", "--volume", $"{root}:/src", "--volume", $"{artifacts}:/artifacts",
                "--workdir", "/src", "--env", "NUGET_PACKAGES=/artifacts/nuget",
                "mcr.microsoft.com/dotnet/sdk:10.0", "bash", "-c", CheckScript],
            root, Path.Combine(artifacts, "checks-container.log"), cancellationToken);

        Dictionary<string, StageCheckResult> checks = new(StringComparer.Ordinal);
        foreach (string name in new[] { "restore", "format-whitespace", "format-style", "build", "test" })
        {
            string log = Path.Combine(artifacts, name + ".log");
            string exit = Path.Combine(artifacts, name + ".exit");
            int code = File.Exists(exit) && int.TryParse(await File.ReadAllTextAsync(exit, cancellationToken), out int value)
                ? value : 125;
            string summary = File.Exists(log) ? Tail(await File.ReadAllTextAsync(log, cancellationToken))
                : $"Check did not run; Docker exited {container.ExitCode}: {Tail(container.Output)}";
            checks[name] = new StageCheckResult(code == 0, code, log, summary);
        }

        string testLog = Path.Combine(artifacts, "test.log");
        Match counts = File.Exists(testLog)
            ? TestSummaryRegex().Match(await File.ReadAllTextAsync(testLog, cancellationToken))
            : Match.Empty;
        int? total = counts.Success ? int.Parse(counts.Groups[1].Value) : null;
        int? failed = counts.Success
            ? int.Parse(counts.Groups[2].Value) + int.Parse(counts.Groups[3].Value) : null;

        return new M3Result(snapshot.HeadSha.Value, artifacts, checks, total, failed,
            null, false,
            "The bot requires Telegram credentials, a database and other external services; no isolated live stand is configured.",
            null, null);
    }

    private static string Tail(string value) => value.Length <= 4000 ? value : value[^4000..];

    [GeneratedRegex(@"Total:\s*(\d+),\s*Errors:\s*(\d+),\s*Failed:\s*(\d+)")]
    private static partial Regex TestSummaryRegex();

    private static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, string logPath, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
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

        string output = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(logPath, output, cancellationToken);
        return new ProcessResult(process.ExitCode, output);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
