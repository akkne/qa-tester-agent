using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Infrastructure.Execution;
using QaAgent.Infrastructure.Llm;

namespace QaAgent.Infrastructure.Analysis;

/// <summary>
/// Lets a Codex agent write tests in the repository's own stack (C# included) inside the test-runner container. The agent's JSON
/// only names the files: the service checks paths, tracked-file integrity, skips and assertions itself and takes the
/// compile verdict from the qa-run ledger. Accepted files stay in the workspace as untracked files for M3.
/// </summary>
public sealed partial class AgentTestWriter(
    string artifactRoot,
    AgentExecutionOptions options,
    ICodexActivityStore activity,
    ILogger<AgentTestWriter> logger) : IM2TestWriter
{
    private const int MaxFiles = 30;
    private const int MaxSourceLength = 60_000;
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);
    // The container can write the repository; host git must not run hooks or an fsmonitor it planted.
    private static readonly string[] SafeGit = ["-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null"];
    private static readonly string[] Kinds = ["positive", "negative", "boundary", "access-control", "support"];
    private readonly CodexContainer codex = new(options, activity, logger);

    public async Task<TestWritingResult> WriteAsync(TestWritingRequest request, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(request.Snapshot.WorkspacePath);
        string head = request.Snapshot.HeadSha.Value;
        Guid runId = request.RunId ?? Guid.NewGuid();
        string evidence = Path.GetFullPath(Path.Combine(artifactRoot, runId.ToString("N"), "authoring"));
        // A repeated pass must never read evidence left by the previous one.
        if (Directory.Exists(evidence)) Directory.Delete(evidence, recursive: true);
        Directory.CreateDirectory(Path.Combine(evidence, "commands"));
        await ResetAsync(root, head, cancellationToken);

        CodexContainerResult run = await codex.RunAsync(root, evidence,
            AgentTestWritingContract.Prompt(request.Profile.AllowedTestDirectories, request.RequirementIds,
                request.SpecificationChanged, options.Hints, options.CommandTimeoutSeconds),
            AgentTestWritingContract.Schema,
            new Dictionary<string, string> { [Path.GetFileName(AgentTestWritingContract.TaskFile)] = TaskText(request) },
            runId, "Test writing", "Test-writing agent", cancellationToken);
        try
        {
            return await CollectAsync(request, root, evidence, run, cancellationToken);
        }
        catch
        {
            await ResetAsync(root, head, CancellationToken.None);
            throw;
        }
        finally
        {
            CodexContainer.PruneEvidence(evidence, "commands", "ledger.jsonl", "agent-report.json", "container.log");
        }
    }

    // Separate from the container run so the checks can be tested without Docker.
    internal async Task<TestWritingResult> CollectAsync(TestWritingRequest request, string root, string evidence,
        CodexContainerResult run, CancellationToken cancellationToken)
    {
        string head = request.Snapshot.HeadSha.Value;
        TestWritingResult Rejected(string reason)
        {
            logger.LogWarning("Agent-written tests rejected: {Reason}", reason);
            return new([], new TestBuildResult(false, reason), [], run.InputTokens, run.OutputTokens);
        }

        AgentWrittenTests? report = null;
        if (run.Answer is not null)
        {
            await CodexContainer.WriteOwnedAsync(Path.Combine(evidence, "agent-report.json"), run.Answer, cancellationToken);
            try
            {
                report = JsonSerializer.Deserialize<AgentWrittenTests>(run.Answer, ReportJson);
            }
            catch (JsonException)
            {
                report = null;
            }
        }

        if (report is not { Status: not null, Files: not null } ||
            report.Files.Any(file => file is not { Path: not null, Kind: not null, RequirementIds: not null, Suite: not null }))
        {
            await ResetAsync(root, head, cancellationToken);
            return Rejected(run.Failure ?? "The test-writing agent returned no valid JSON report.");
        }

        // Diff against the expected commit, so a commit made inside the container cannot hide edits.
        ProcessResult moved = await GitAsync(root, ["rev-parse", "HEAD"], cancellationToken);
        ProcessResult diff = await GitAsync(root, ["diff", "--name-only", head, "--"], cancellationToken);
        if (moved.ExitCode != 0 || diff.ExitCode != 0 ||
            !string.Equals(moved.StandardOutput.Trim(), head, StringComparison.OrdinalIgnoreCase) ||
            diff.StandardOutput.Trim().Length > 0)
        {
            await ResetAsync(root, head, cancellationToken);
            return Rejected("The test-writing agent modified tracked files or moved HEAD: " +
                CodexContainer.Tail(diff.StandardOutput.Trim() + " " + moved.StandardOutput.Trim()));
        }

        ProcessResult listed = await GitAsync(root, ["ls-files", "-z"], cancellationToken);
        if (listed.ExitCode != 0)
            throw new InvalidOperationException("Could not list tracked files: " + CodexContainer.Tail(listed.Output));
        HashSet<string> tracked = listed.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> knownIds = request.RequirementIds.ToHashSet(StringComparer.Ordinal);
        List<string> gaps = [];
        Dictionary<string, (AgentWrittenFile File, string Source)> accepted = new(StringComparer.Ordinal);
        foreach (AgentWrittenFile file in report.Files.Take(MaxFiles))
        {
            string path = file.Path.Replace('\\', '/');
            if (path.StartsWith(AgentTestContract.SourceMount + "/", StringComparison.Ordinal))
                path = path[(AgentTestContract.SourceMount.Length + 1)..];
            string? problem = await CheckAsync(request, root, tracked, knownIds, file, path, cancellationToken);
            if (problem is not null)
            {
                gaps.Add($"Agent-written file {path} was discarded: {problem}");
                continue;
            }

            accepted.TryAdd(path, (file, await File.ReadAllTextAsync(Path.Combine(root, path), cancellationToken)));
        }

        if (report.Files.Count > MaxFiles)
            gaps.Add($"The test-writing agent returned {report.Files.Count} files; only the first {MaxFiles} were checked.");
        if (report.Notes is { Length: > 0 } notes)
            gaps.Add("Test-writing agent notes: " + (notes.Length <= 1500 ? notes : notes[..1500] + "…"));

        // The workspace keeps only the accepted files: build output and undeclared files of the agent are removed.
        await ResetAsync(root, head, cancellationToken);
        List<GeneratedTest> tests = [];
        foreach ((string path, (AgentWrittenFile file, string source)) in accepted)
        {
            string full = Path.GetFullPath(Path.Combine(root, path));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (WorkspacePath.HasLink(root, full))
            {
                gaps.Add($"Agent-written file {path} was discarded: its directory is a link.");
                continue;
            }

            await File.WriteAllTextAsync(full, source, cancellationToken);
            tests.Add(new GeneratedTest(path, file.RequirementIds.Where(knownIds.Contains).Distinct(StringComparer.Ordinal)
                .ToArray(), file.Kind, source));
        }

        if (tests.Count == 0)
            return new([], new TestBuildResult(report.Status != "blocked" && report.Files.Count == 0,
                    report.Status == "blocked"
                        ? "The test-writing agent was blocked: " + (report.BlockReason ?? "no reason given")
                        : report.Files.Count == 0 ? "" : "Every agent-written file was discarded."),
                gaps, run.InputTokens, run.OutputTokens);
        if (tests.All(test => test.Kind == "support"))
            return new([], new TestBuildResult(false, "The test-writing agent wrote only support files."), gaps,
                run.InputTokens, run.OutputTokens);

        List<string> warnings = [];
        IReadOnlyList<CommandRecord> ledger = TestReportParser.ReadLedger(
            Path.Combine(evidence, "ledger.jsonl"), evidence, warnings);
        gaps.AddRange(warnings);
        List<string> diagnostics = [];
        int fixes = 0;
        foreach (string suite in tests.Select(test => accepted[test.Path].File.Suite).Distinct(StringComparer.Ordinal))
        {
            CommandRecord[] builds = ledger.Where(record => record.Suite == suite && record.Phase == "build")
                .OrderBy(record => record.Sequence).ToArray();
            CommandRecord? build = builds.LastOrDefault();
            // Timeouts, missing tools, SIGILL and OOM kills are environment problems, not repaired test code.
            fixes += builds.SkipLast(1).Count(record => record.ExitCode is not (0 or 124 or 126 or 127 or 132 or 137));
            if (build is null)
                diagnostics.Add($"Suite '{suite}': the agent never compiled the tests through qa-run --phase build.");
            else if (build.ExitCode != 0)
                diagnostics.Add($"Suite '{suite}': build exited {build.ExitCode} ({build.Command}):\n" +
                    await ReadTailAsync(build.LogPath, cancellationToken));
        }

        return new(tests, new TestBuildResult(diagnostics.Count == 0, string.Join("\n", diagnostics)), gaps,
            run.InputTokens, run.OutputTokens, fixes);
    }

    private static async Task<string?> CheckAsync(TestWritingRequest request, string root, HashSet<string> tracked,
        HashSet<string> knownIds, AgentWrittenFile file, string path, CancellationToken cancellationToken)
    {
        if (!request.Profile.AllowsTestPath(path))
            return "the path is outside the allowed test directories.";
        if (tracked.Contains(path))
            return "it is a tracked file.";
        string full = Path.GetFullPath(Path.Combine(root, path));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            full.Split(Path.DirectorySeparatorChar).Contains(".git", StringComparer.Ordinal) || WorkspacePath.HasLink(root, full))
            return "the path escapes the workspace.";
        if (!File.Exists(full))
            return "the file does not exist.";
        if (new FileInfo(full).Length > MaxSourceLength * 4)
            return "the file is too large.";
        if (!Kinds.Contains(file.Kind, StringComparer.Ordinal))
            return $"unknown kind {file.Kind}.";
        if (!SuiteRegex().IsMatch(file.Suite))
            return "the suite id is invalid.";
        string source = await File.ReadAllTextAsync(full, cancellationToken);
        if (source.Length > MaxSourceLength || source.Contains('\0', StringComparison.Ordinal))
            return "the file is too large or not text.";
        if (SkipRegex().IsMatch(source))
            return "it skips, disables or focuses tests.";
        if (file.Kind == "support")
            return null;
        if (!file.RequirementIds.Any(knownIds.Contains))
            return "it has no known requirement ID.";
        return AssertionRegex().IsMatch(source) ? null : "it contains no assertion.";
    }

    private static string TaskText(TestWritingRequest request)
    {
        StringBuilder text = new();
        text.AppendLine("# Test-writing task").AppendLine();
        text.AppendLine("Requirement IDs to cover: " + string.Join(", ", request.RequirementIds)).AppendLine();
        text.AppendLine("## Verification plan (JSON)").AppendLine(request.PlanJson).AppendLine();
        text.AppendLine("## Analysis findings (JSON; hypotheses, not confirmed defects)").AppendLine(request.FindingsJson).AppendLine();
        text.AppendLine("## Context: specification, Git diff, changed sources and existing tests").AppendLine(request.Context);
        return text.ToString();
    }

    private static async Task ResetAsync(string root, string head, CancellationToken cancellationToken)
    {
        ProcessResult checkout = await GitAsync(root, ["checkout", "--force", "--detach", head], cancellationToken);
        ProcessResult clean = await GitAsync(root, ["clean", "-fdxq"], cancellationToken);
        if (checkout.ExitCode != 0 || clean.ExitCode != 0)
            throw new InvalidOperationException("Could not reset the workspace after the test-writing agent: " +
                CodexContainer.Tail(checkout.Output + clean.Output));
    }

    private static Task<ProcessResult> GitAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync("git", [.. SafeGit, "-C", root, .. arguments], null, cancellationToken);

    private static async Task<string> ReadTailAsync(string path, CancellationToken cancellationToken)
    {
        if (path.Length == 0 || !File.Exists(path)) return "(no log)";
        await using FileStream stream = File.OpenRead(path);
        stream.Seek(Math.Max(0, stream.Length - 8000), SeekOrigin.Begin);
        using StreamReader reader = new(stream);
        return CodexContainer.Tail(await reader.ReadToEndAsync(cancellationToken));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex SuiteRegex();

    // Skipped, disabled or focused tests in the common frameworks; a focused test silently disables its siblings.
    [GeneratedRegex("""\bSkip\s*=|\[Skip\b|\bAssert\.True\s*\(\s*true\s*\)|@pytest\.mark\.(skip|skipif|xfail)\b|\bpytest\.(skip|xfail)\s*\(|@unittest\.(skip|expectedFailure)|\b(it|test|describe|context)\.(skip|only|todo)\b|\b(xit|xtest|xdescribe|xcontext|fit|fdescribe)\s*\(\s*['"`]|@Disabled\b|@Ignore\b|#\[ignore\b|\bt\.Skip(Now|f)?\s*\(|markTest(Skipped|Incomplete)|\bpending\s*\(|\bskip\s*\(\s*['"]""")]
    private static partial Regex SkipRegex();

    // Expected exceptions count too: pytest.raises, assertRaises, toThrow, Assert.Throws, assertThrows.
    [GeneratedRegex(@"(?i)assert|expect|should|\braises\b|\bthrows?\b|tothrow|\bt\.(error|errorf|fatal|fatalf|fail)\b|\brequire\.|\bverify\b|\bmust\b|#\[should_panic")]
    private static partial Regex AssertionRegex();
}
