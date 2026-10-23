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
        run_check build dotnet build LeadQualification.slnx --no-restore --nologo -warnaserror
        run_check test dotnet run --project tests/LeadQualification.Tests/LeadQualification.Tests.csproj --no-build --no-restore
        smoke_host() {
          if [ ! -f .env.ci.example ]; then
            echo 'Missing .env.ci.example in the checked-out source.'
            return 125
          fi
          set -a
          . ./.env.ci.example
          set +a
          export ASPNETCORE_ENVIRONMENT=CI
          export ASPNETCORE_URLS=http://127.0.0.1:5088
          dotnet run --project src/LeadQualification.Host --no-build --no-restore --no-launch-profile > /artifacts/host-process.log 2>&1 &
          host_pid=$!
          trap 'kill "$host_pid" 2>/dev/null || true; wait "$host_pid" 2>/dev/null || true' EXIT
          for attempt in $(seq 1 30); do
            status=$(curl --silent --max-time 2 --output /dev/null --write-out '%{http_code}' http://127.0.0.1:5088/health) || status=000
            if [ "$status" = 200 ]; then
              echo 'Host /health returned HTTP 200.'
              return 0
            fi
            if ! kill -0 "$host_pid" 2>/dev/null; then
              echo 'Host exited before /health returned HTTP 200.'
              cat /artifacts/host-process.log
              return 1
            fi
            sleep 1
          done
          echo 'Host /health did not return HTTP 200 within 30 seconds.'
          cat /artifacts/host-process.log
          return 1
        }
        run_check host-smoke smoke_host
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
        // Host-side M2 builds leave macOS bin/obj output; the Linux container must build from clean sources.
        await RunAsync("git", ["-C", root, "clean", "-fdXq"], root,
            Path.Combine(artifacts, "clean-build-output.log"), cancellationToken);

        // A repeated pass must never read check results left by the previous one.
        foreach (string name in new[] { "restore", "build", "test", "host-smoke" })
        {
            File.Delete(Path.Combine(artifacts, name + ".log"));
            File.Delete(Path.Combine(artifacts, name + ".exit"));
        }

        ProcessResult container = await RunAsync("docker",
            ["run", "--rm", "--volume", $"{root}:/src", "--volume", $"{artifacts}:/artifacts",
                "--workdir", "/src", "--env", "NUGET_PACKAGES=/artifacts/nuget",
                "mcr.microsoft.com/dotnet/sdk:10.0", "bash", "-c", CheckScript],
            root, Path.Combine(artifacts, "checks-container.log"), cancellationToken);

        Dictionary<string, StageCheckResult> checks = new(StringComparer.Ordinal);
        foreach (string name in new[] { "restore", "build", "test", "host-smoke" })
        {
            string log = Path.Combine(artifacts, name + ".log");
            string exit = Path.Combine(artifacts, name + ".exit");
            int code = File.Exists(exit) && File.Exists(log) &&
                int.TryParse(await File.ReadAllTextAsync(exit, cancellationToken), out int value)
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
            null, null, M3ExecutionProfile.LeadQualification);
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
