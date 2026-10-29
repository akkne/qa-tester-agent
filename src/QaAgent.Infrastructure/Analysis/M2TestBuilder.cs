using System.Diagnostics;
using System.Text.RegularExpressions;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;

namespace QaAgent.Infrastructure.Analysis;

public sealed partial class M2TestBuilder : IM2TestBuilder
{
    private readonly HashSet<string> generatedPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> baselineAssertions = new(StringComparer.Ordinal);

    public async Task<TestBuildResult> BuildAsync(
        GitSnapshot snapshot,
        ProjectProfile profile,
        IReadOnlyList<GeneratedTest> tests,
        CancellationToken cancellationToken)
    {
        if (tests.Count == 0)
        {
            throw new InvalidOperationException("No tests were generated.");
        }

        string root = Path.GetFullPath(snapshot.WorkspacePath);
        Dictionary<string, string> next = new(StringComparer.Ordinal);
        foreach (GeneratedTest test in tests)
        {
            string normalized = test.Path.Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.Split('/').Contains("..", StringComparer.Ordinal) ||
                !normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                !profile.AllowedTestDirectories.Any(directory =>
                    normalized.StartsWith(directory.TrimEnd('/') + "/", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Generated test path is forbidden: {test.Path}.");
            }

            string path = Path.GetFullPath(Path.Combine(root, normalized));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || WorkspacePath.HasLink(root, path) ||
                (File.Exists(path) && !generatedPaths.Contains(path)) || !next.TryAdd(path, test.Source))
            {
                throw new InvalidOperationException($"Generated test would overwrite an existing or duplicate file: {test.Path}.");
            }

            if (test.Source.Length > 40_000 || SkipRegex().IsMatch(test.Source) ||
                !TestAttributeRegex().IsMatch(test.Source) || !AssertRegex().IsMatch(test.Source))
            {
                throw new InvalidOperationException($"Generated test lacks an assertion or contains Skip: {test.Path}.");
            }
        }

        Dictionary<string, HashSet<string>> assertions = next.ToDictionary(
            item => item.Key,
            item => AssertionRegex().Matches(item.Value)
                .Select(match => Normalize(match.Value)).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        if (baselineAssertions.Count == 0)
        {
            foreach ((string path, HashSet<string> statements) in assertions)
            {
                baselineAssertions.Add(path, statements);
            }
        }
        else if (baselineAssertions.Any(item =>
            !assertions.TryGetValue(item.Key, out HashSet<string>? statements) ||
            !item.Value.IsSubsetOf(statements)))
        {
            throw new InvalidOperationException("A generation fix removed or changed an assertion.");
        }

        foreach (string old in generatedPaths)
        {
            File.Delete(old);
        }

        generatedPaths.Clear();
        foreach ((string path, string source) in next)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, source, cancellationToken);
            generatedPaths.Add(path);
        }

        string[] projects = profile.AllowedTestDirectories
            .Select(directory => Path.Combine(root, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (projects.Length == 0)
        {
            throw new InvalidOperationException("No test project found under allowed test directories.");
        }

        // The bot's xUnit v3 project resolves its full reference graph when built through the solution.
        string solution = Path.Combine(root, "LeadQualification.slnx");
        string[] buildTargets = File.Exists(solution) ? [solution] : projects;
        List<string> diagnostics = [];
        foreach (string project in buildTargets)
        {
            ProcessStartInfo start = new("dotnet")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            // The pushed repo is untrusted: its build must not inherit the agent's secrets.
            foreach (string name in start.Environment.Keys.Where(IsSecretName).ToArray())
            {
                start.Environment.Remove(name);
            }

            start.ArgumentList.Add("build");
            start.ArgumentList.Add(project);
            start.ArgumentList.Add("--nologo");
            start.ArgumentList.Add("-v:q");
            start.ArgumentList.Add("-warnaserror");
            start.ArgumentList.Add("-m:1");
            start.ArgumentList.Add("/p:UseSharedCompilation=false");
            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("Unable to start dotnet build.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }

            string output = await stdout + await stderr;
            if (process.ExitCode != 0)
            {
                diagnostics.Add($"{Path.GetRelativePath(root, project)}: {output[..Math.Min(output.Length, 8000)]}");
            }
        }

        return diagnostics.Count == 0
            ? new TestBuildResult(true, string.Empty)
            : new TestBuildResult(false, string.Join("\n", diagnostics));
    }

    public void Cleanup()
    {
        foreach (string path in generatedPaths)
        {
            File.Delete(path);
        }

        generatedPaths.Clear();
    }

    private static bool IsSecretName(string name) =>
        SecretEnvironmentRegex().IsMatch(name) ||
        name.StartsWith("GitHub__", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("AgentApi__", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("TestAgent__", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Llm__", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string text) => WhiteSpaceRegex().Replace(text, string.Empty);

    [GeneratedRegex(@"\bSkip\s*=|\[Skip\b|\bAssert\.True\s*\(\s*true\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex SkipRegex();

    [GeneratedRegex(@"\[(Fact|Theory)\b")]
    private static partial Regex TestAttributeRegex();

    [GeneratedRegex(@"\bAssert\.\w+\s*\(")]
    private static partial Regex AssertRegex();

    [GeneratedRegex(@"\bAssert\.\w+\s*\([^;]*;", RegexOptions.Singleline)]
    private static partial Regex AssertionRegex();

    [GeneratedRegex("TOKEN|KEY|SECRET|PASSWORD|CREDENTIAL", RegexOptions.IgnoreCase)]
    private static partial Regex SecretEnvironmentRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpaceRegex();
}
