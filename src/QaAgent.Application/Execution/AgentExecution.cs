namespace QaAgent.Application.Execution;

public enum SuiteVerdict { Passed, Failed, Flaky, Unverified, Blocked }

public sealed record SuiteResult(
    string Id,
    string Language,
    string Framework,
    string WorkingDirectory,
    SuiteVerdict Verdict,
    string? Reason,
    int? Total,
    int? Failed,
    int? Skipped,
    bool CountsVerified,
    IReadOnlyList<string> FailedTests,
    string FailureSummary,
    string Notes);

/// <summary>Suite verdicts derived from qa-run records and parsed reports; the agent's own claims are only cross-checked.</summary>
public sealed record AgentExecution(
    IReadOnlyList<SuiteResult> Suites,
    IReadOnlyList<AgentSkippedSuite> NotRun,
    IReadOnlyList<string> Warnings,
    string? BlockReason,
    int InputTokens,
    int OutputTokens)
{
    public static AgentExecution Evaluate(
        AgentTestReport? report,
        IReadOnlyList<CommandRecord> ledger,
        IReadOnlyDictionary<string, IReadOnlyList<ParsedTestReport>> reportsBySuite,
        IReadOnlyList<string> modifiedTrackedFiles,
        int inputTokens,
        int outputTokens)
    {
        List<string> warnings = [];
        if (report is null)
            return new AgentExecution([], [], warnings, "The test agent returned no valid JSON report.", inputTokens, outputTokens);

        string? blockReason = modifiedTrackedFiles.Count > 0
            ? "The test agent modified tracked files: " + string.Join(", ", modifiedTrackedFiles.Take(20))
            : null;
        if (report.Status == "blocked")
            warnings.Add("Agent reported blocked: " + (report.BlockReason ?? "no reason given"));

        List<AgentSuite> declared = report.Suites
            .GroupBy(suite => suite.Id, StringComparer.Ordinal).Select(group => group.First()).ToList();
        // Commands under an undeclared suite id are still judged, so an omitted failing suite cannot disappear.
        foreach (string id in ledger.Select(record => record.Suite).Distinct(StringComparer.Ordinal)
                     .Where(id => declared.All(suite => suite.Id != id)))
        {
            warnings.Add($"Suite '{id}' has qa-run records but is missing from the agent report.");
            declared.Add(new AgentSuite(id, "unknown", "unknown", ".", [], [], null, "", ""));
        }

        List<SuiteResult> suites = [];
        foreach (AgentSuite suite in declared)
        {
            IReadOnlyList<ParsedTestReport> reports = reportsBySuite.GetValueOrDefault(suite.Id) ?? [];
            foreach (ParsedTestReport broken in reports.Where(item => item.Error is not null))
                warnings.Add($"Suite '{suite.Id}': report {broken.Path} was not usable: {broken.Error}");
            SuiteResult result = Judge(suite, ledger.Where(record => record.Suite == suite.Id)
                .OrderBy(record => record.Sequence).ToArray(), reports.Where(item => item.Error is null).ToArray());
            if (suite.Counts is { } claimed && result.CountsVerified && result.Total is { } total &&
                (claimed.Total != total || claimed.Failed != result.Failed))
                warnings.Add($"Suite '{suite.Id}': agent reported {claimed.Total} total/{claimed.Failed} failed, " +
                    $"reports contain {total}/{result.Failed}.");
            suites.Add(result);
        }

        if (suites.Count == 0 && blockReason is null)
            blockReason = "The test agent found no runnable test suites" +
                (report.BlockReason is { } reason ? ": " + reason : ".");
        return new AgentExecution(suites, report.NotRun, warnings, blockReason, inputTokens, outputTokens);
    }

    private static SuiteResult Judge(AgentSuite suite, CommandRecord[] records, ParsedTestReport[] reports)
    {
        SuiteResult Result(SuiteVerdict verdict, string? reason, bool withCounts = true) => new(
            suite.Id, suite.Language, suite.Framework, suite.WorkingDirectory, verdict, reason,
            withCounts && reports.Length > 0 ? reports.Sum(item => item.Total) : suite.Counts?.Total,
            withCounts && reports.Length > 0 ? reports.Sum(item => item.Failed) : suite.Counts?.Failed,
            withCounts && reports.Length > 0 ? reports.Sum(item => item.Skipped) : suite.Counts?.Skipped,
            withCounts && reports.Length > 0,
            reports.SelectMany(item => item.FailedTests).Take(50).ToArray(), suite.FailureSummary, suite.Notes);

        CommandRecord[] tests = records.Where(record => record.Phase == "test").ToArray();
        CommandRecord? setup = records.LastOrDefault(record => record.Phase == "setup");
        CommandRecord? build = records.LastOrDefault(record => record.Phase == "build");
        if (records.Length == 0)
            return Result(SuiteVerdict.Blocked, "No qa-run record exists for this suite.", false);
        // A compile error judges the code; a failed dependency install only matters when no test run followed it.
        if (build is { ExitCode: not 0 } && !IsEnvironmentExit(build.ExitCode))
            return Result(SuiteVerdict.Failed, $"Build failed (exit {build.ExitCode}).", false);
        if (tests.Length == 0)
            return Result(SuiteVerdict.Blocked, setup is { ExitCode: not 0 }
                ? $"Setup failed (exit {setup.ExitCode}); tests did not run."
                : build is { ExitCode: not 0 }
                    ? $"Build could not run (exit {build.ExitCode}); tests did not run."
                    : "The test command was never recorded.", false);

        CommandRecord last = tests[^1];
        int failedTests = reports.Sum(item => item.Failed);
        if (reports.Length > 0 && failedTests > 0)
            return Result(SuiteVerdict.Failed, $"{failedTests} tests failed (exit {last.ExitCode}).");
        if (IsEnvironmentExit(last.ExitCode))
            return Result(SuiteVerdict.Blocked, last.ExitCode == 124
                ? "The test command timed out." : $"The test command could not run (exit {last.ExitCode}).");
        if (last.ExitCode != 0)
            return Result(SuiteVerdict.Failed, $"The test command exited {last.ExitCode}.");
        if (tests[..^1].Any(record => record.ExitCode != 0 && !IsEnvironmentExit(record.ExitCode)))
            return Result(SuiteVerdict.Flaky, "The test command failed before a later rerun passed.");
        if (reports.Length == 0)
            return Result(SuiteVerdict.Unverified, "No JUnit or TRX report; counts are agent-reported.");
        if (reports.Sum(item => item.Total) == 0)
            return Result(SuiteVerdict.Blocked, "The reports contain no executed tests.");
        return Result(SuiteVerdict.Passed, null);
    }

    // 124 timeout, 126 not executable, 127 not found, 137 killed (usually out of memory): not a verdict on the code.
    private static bool IsEnvironmentExit(int exitCode) => exitCode is 124 or 126 or 127 or 137;
}
