using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml.Linq;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;

namespace QaAgent.Infrastructure.Execution;

public sealed class DockerM3Executor(
    string artifactRoot,
    ICoverageCalculator coverageCalculator) : IM3Executor
{
    private const string Image = "mcr.microsoft.com/dotnet/sdk:10.0";
    private const string CheckScript = """
        run_check() {
          name="$1"; shift
          "$@" >"/artifacts/$name.log" 2>&1
          printf '%s' "$?" >"/artifacts/$name.exit"
        }
        run_check restore dotnet restore QaAgent.slnx --nologo
        run_check format-whitespace dotnet format whitespace QaAgent.slnx --verify-no-changes --no-restore
        run_check format-style dotnet format style QaAgent.slnx --verify-no-changes --no-restore
        run_check build dotnet build QaAgent.slnx --no-restore --nologo -warnaserror
        run_check test dotnet test QaAgent.slnx --no-restore --collect 'XPlat Code Coverage' --logger trx --results-directory /artifacts/test-results
        """;

    public async Task<M3Result> ExecuteAsync(
        GitSnapshot snapshot, Guid runId, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(snapshot.WorkspacePath);
        string artifacts = Path.GetFullPath(Path.Combine(artifactRoot, runId.ToString("N")));
        Directory.CreateDirectory(artifacts);
        string actualSha = (await RunAsync("git", ["-C", root, "rev-parse", "HEAD"], root,
            Path.Combine(artifacts, "source-sha.log"), null, cancellationToken)).Output.Trim();
        if (!string.Equals(actualSha, snapshot.HeadSha.Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The test workspace does not match headSha.");
        }

        // A repeated pass must never read check results left by the previous one.
        foreach (string name in new[] { "restore", "format-whitespace", "format-style", "build", "test" })
        {
            File.Delete(Path.Combine(artifacts, name + ".log"));
            File.Delete(Path.Combine(artifacts, name + ".exit"));
        }

        string testResults = Path.Combine(artifacts, "test-results");
        if (Directory.Exists(testResults)) Directory.Delete(testResults, recursive: true);

        Dictionary<string, StageCheckResult> checks = new(StringComparer.Ordinal);
        string containerName = $"qa-checks-{runId.ToString("N")[..12]}-{Guid.NewGuid().ToString("N")[..6]}";
        string[] dockerArgs = ["run", "--rm", "--name", containerName, "--volume", $"{root}:/src",
            "--volume", $"{artifacts}:/artifacts", "--workdir", "/src",
            "--env", "NUGET_PACKAGES=/artifacts/nuget", Image, "bash", "-c", CheckScript];
        ProcessResult container;
        try
        {
            container = await RunAsync("docker", dockerArgs, root,
                Path.Combine(artifacts, "checks-container.log"), null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Killing the docker client does not stop the container.
            await RunAsync("docker", ["rm", "--force", containerName], root,
                Path.Combine(artifacts, "checks-container-stop.log"), null, CancellationToken.None);
            throw;
        }
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

        string[] cobertura = Directory.Exists(testResults)
            ? Directory.GetFiles(testResults, "coverage.cobertura.xml", SearchOption.AllDirectories)
            : [];
        CoverageResult coverage = await coverageCalculator.CalculateAsync(snapshot, cobertura, cancellationToken);
        (int? totalTests, int? failedTests) = TestCounts(artifacts);

        int port = FreePort();
        string composeProject = "qa-m3-" + runId.ToString("N")[..12];
        string composeFile = Path.Combine(root, "demo", "compose.yaml");
        string[] composePrefix = ["compose", "-p", composeProject, "-f", composeFile];
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["M3_HEAD_SHA"] = snapshot.HeadSha.Value,
            ["M3_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        bool standAvailable = false;
        string? standFailure = null;
        bool? browserPassed = null;
        string? browserJson = null;
        try
        {
            ProcessResult started = await RunAsync("docker", [.. composePrefix, "up", "--build", "-d", "db", "app"],
                root, Path.Combine(artifacts, "stand-start.log"), environment, cancellationToken);
            if (started.ExitCode != 0)
            {
                standFailure = "Docker Compose could not start the stand: " + Tail(started.Output);
            }
            else
            {
                standAvailable = await WaitForStandAsync(port, snapshot.HeadSha.Value, cancellationToken);
                if (!standAvailable)
                {
                    standFailure = "The stand did not become healthy with the requested headSha.";
                }
            }

            if (standAvailable)
            {
                string browserDir = Path.Combine(root, "browser");
                if (!File.Exists(Path.Combine(browserDir, "package-lock.json")) ||
                    !File.Exists(Path.Combine(browserDir, "run.mjs")))
                {
                    standFailure = "The checked-out SHA has no browser runner.";
                    standAvailable = false;
                }
                else
                {
                    ProcessResult install = await RunAsync("npm", ["ci", "--prefix", browserDir], root,
                        Path.Combine(artifacts, "browser-install.log"), null, cancellationToken);
                    ProcessResult chromium = install.ExitCode == 0
                        ? await RunAsync("npx", ["--prefix", browserDir, "playwright", "install", "chromium"], root,
                            Path.Combine(artifacts, "chromium-install.log"), null, cancellationToken)
                        : new ProcessResult(125, install.Output);
                    if (chromium.ExitCode != 0)
                    {
                        standFailure = "Chromium prerequisite failed: " + Tail(chromium.Output);
                        standAvailable = false;
                    }
                    else
                    {
                        ProcessResult browser = await RunAsync("node",
                            [Path.Combine(browserDir, "run.mjs"), "--base-url", $"http://127.0.0.1:{port}",
                                "--artifacts", Path.Combine(artifacts, "browser")], root,
                            Path.Combine(artifacts, "browser.log"), null, cancellationToken);
                        browserJson = browser.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .LastOrDefault(line => line.TrimStart().StartsWith('{'));
                        browserPassed = browser.ExitCode == 0;
                    }
                }
            }
        }
        finally
        {
            using CancellationTokenSource cleanup = new(TimeSpan.FromMinutes(2));
            await RunAsync("docker", [.. composePrefix, "down", "--volumes", "--remove-orphans"], root,
                Path.Combine(artifacts, "stand-stop.log"), environment, cleanup.Token);
        }

        return new M3Result(snapshot.HeadSha.Value, artifacts, checks, totalTests, failedTests,
            coverage, standAvailable, standFailure, browserPassed, browserJson);
    }

    private static async Task<bool> WaitForStandAsync(int port, string expectedSha, CancellationToken cancellationToken)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
        for (int attempt = 0; attempt < 45; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpResponseMessage response = await client.GetAsync(
                    $"http://127.0.0.1:{port}/healthz", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    return document.RootElement.GetProperty("sourceSha").GetString() == expectedSha;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return false;
    }

    private static (int?, int?) TestCounts(string artifacts)
    {
        string directory = Path.Combine(artifacts, "test-results");
        if (!Directory.Exists(directory)) return (null, null);
        int total = 0;
        int failed = 0;
        string[] files = Directory.GetFiles(directory, "*.trx", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            XElement? counters = XDocument.Load(file).Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Counters");
            if (counters is null) continue;
            total += int.TryParse(counters.Attribute("total")?.Value, out int count) ? count : 0;
            failed += int.TryParse(counters.Attribute("failed")?.Value, out int failedCount) ? failedCount : 0;
        }

        return files.Length == 0 ? (null, null) : (total, failed);
    }

    private static int FreePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string Tail(string value) => value.Length <= 4000 ? value : value[^4000..];

    private static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, string logPath, IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach ((string key, string value) in environment) start.Environment[key] = value;
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
