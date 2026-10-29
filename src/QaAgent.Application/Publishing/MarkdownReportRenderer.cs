using System.Text;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Domain;

namespace QaAgent.Application.Publishing;

public static class MarkdownReportRenderer
{
    public static string Render(Run run, GitSnapshot snapshot, M2Result analysis,
        M3Result execution, RunStatus status, IReadOnlyList<PublishedArtifact> artifacts,
        string resultBaseUrl)
    {
        StringBuilder text = new();
        string localUrl = resultBaseUrl.TrimEnd('/') + "/runs/" + run.Id;
        text.AppendLine($"# QA result for `{snapshot.HeadSha.Value[..12]}`");
        text.AppendLine();
        text.AppendLine($"- **Status:** {status}");
        text.AppendLine($"- **Repository:** {Safe(run.RepositoryId)}");
        text.AppendLine($"- **Range:** `{snapshot.BaseSha.Value}..{snapshot.HeadSha.Value}`");
        text.AppendLine($"- **Run ID:** `{run.Id}`");
        text.AppendLine($"- **Result page:** {localUrl}");
        if (run.StartedAt is { } started)
            text.AppendLine($"- **Duration:** {Math.Max(0, (DateTimeOffset.UtcNow - started).TotalSeconds):F0} seconds (at report creation)");
        text.AppendLine($"- **LLM tokens:** {analysis.InputTokens} input, {analysis.OutputTokens} output; {analysis.GenerationFixes} generation fixes");
        text.AppendLine();
        text.AppendLine("## Requirements and changed files");
        text.AppendLine();
        foreach (string id in analysis.RequirementIds) text.AppendLine($"- {Code(id)}");
        foreach (ChangedFile file in snapshot.Files) text.AppendLine($"- {Code(file.ChangeKind + " " + file.Path)}");
        text.AppendLine();
        text.AppendLine("## Verification plan");
        text.AppendLine();
        foreach (VerificationScenario scenario in analysis.Plan)
            text.AppendLine($"- {Code(scenario.RequirementId)} {Safe(scenario.Kind)}: {Safe(scenario.Expected)} — {Safe(scenario.Steps)}");
        text.AppendLine();
        text.AppendLine("## Findings");
        text.AppendLine();
        if (analysis.Findings.Count == 0) text.AppendLine("No LLM findings.");
        foreach (AnalysisFinding finding in analysis.Findings)
            text.AppendLine($"- **{Safe(finding.Status)} / {Safe(finding.Severity)}** {Code(finding.Path + ":" + finding.Line)} ({Safe(finding.RequirementId)}): expected {Safe(finding.Expected)}; observed {Safe(finding.Observed)}; reproduce: {Safe(finding.Reproduction)}");
        text.AppendLine();
        text.AppendLine("## Candidate tests and execution");
        text.AppendLine();
        foreach (GeneratedTest test in analysis.Tests)
            text.AppendLine($"- {Code(test.Path)} — {Safe(test.Kind)}; {string.Join(", ", test.RequirementIds.Select(Safe))}");
        text.AppendLine($"- **Tests:** {execution.TotalTests?.ToString() ?? "unavailable"} total; {execution.FailedTests?.ToString() ?? "unavailable"} failed");
        foreach ((string name, StageCheckResult check) in execution.Checks)
            text.AppendLine($"- **{Safe(name)}:** {(check.Passed ? "passed" : "failed")} (exit {check.ExitCode}); {Safe(check.Summary)}");
        if (execution.Profile == M3ExecutionProfile.WebStand)
        {
            text.AppendLine($"- **Stand:** {(execution.StandAvailable ? "available" : "unavailable")}; {Safe(execution.StandFailure ?? "")}");
            text.AppendLine($"- **Browser:** {execution.BrowserPassed?.ToString() ?? "not run"}; {Safe(execution.BrowserJson ?? "")}");
        }
        if (execution.Agent is { } agent)
        {
            text.AppendLine($"- **Test agent:** {agent.Suites.Count} suites; {agent.InputTokens} input, {agent.OutputTokens} output tokens");
            if (agent.BlockReason is not null) text.AppendLine($"- **Test agent blocked:** {Safe(agent.BlockReason)}");
            foreach (SuiteResult suite in agent.Suites)
            {
                string counts = suite.Total is { } total
                    ? $"{total} total, {suite.Failed} failed, {suite.Skipped} skipped{(suite.CountsVerified ? "" : " (agent-reported)")}"
                    : "no counts";
                text.AppendLine($"- **Suite {Code(suite.Id)}** ({Safe(suite.Language)}/{Safe(suite.Framework)}, {Code(suite.WorkingDirectory)}): " +
                    $"{suite.Verdict}; {counts}{(suite.Reason is null ? "" : "; " + Safe(suite.Reason))}");
                foreach (string test in suite.FailedTests.Take(20)) text.AppendLine($"  - failed: {Code(test)}");
                if (suite.FailureSummary.Length > 0) text.AppendLine($"  - agent summary: {Safe(suite.FailureSummary)}");
                if (suite.Notes.Length > 0) text.AppendLine($"  - agent notes: {Safe(suite.Notes)}");
            }
            foreach (AgentSkippedSuite skipped in agent.NotRun)
                text.AppendLine($"- **Not run:** {Code(skipped.Path)} — {Safe(skipped.Reason)}");
            foreach (string warning in agent.Warnings) text.AppendLine($"- **Agent warning:** {Safe(warning)}");
        }
        if (execution.Coverage is { } coverage)
        {
            text.AppendLine($"- **Whole project line coverage:** {Metric(coverage.WholeProject)}");
            text.AppendLine($"- **Changed executable line coverage:** {Metric(coverage.ChangedLines)}");
        }
        text.AppendLine();
        text.AppendLine("## Local evidence");
        text.AppendLine();
        foreach (PublishedArtifact artifact in artifacts)
            text.AppendLine($"- [{Safe(artifact.Name)}]({localUrl}/artifacts/{artifact.Id}) ({artifact.Bytes} bytes)");
        if (analysis.ContextGaps.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Warnings and context gaps");
            text.AppendLine();
            foreach (string gap in analysis.ContextGaps) text.AppendLine($"- {Safe(gap)}");
        }
        return text.ToString();
    }

    private static string Metric(CoverageMetric metric) => metric.Percent is { } percent
        ? $"{percent}% ({metric.CoveredLines}/{metric.ExecutableLines})"
        : $"unavailable: {Safe(metric.Reason ?? "no executable lines")}";

    private static string Code(string? value)
    {
        string text = (value ?? "").Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        int longest = 0, run = 0;
        foreach (char c in text)
            longest = Math.Max(longest, run = c == '`' ? run + 1 : 0);
        string fence = new('`', longest + 1);
        string pad = text.StartsWith('`') || text.EndsWith('`') ? " " : "";
        return fence + pad + text + pad + fence;
    }

    private static string Safe(string? value) => (value ?? "").Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("`", "\\`", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("]", "\\]", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);
}
