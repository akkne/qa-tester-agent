using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Application.Publishing;
using QaAgent.Domain;

namespace QaAgent.UnitTests;

public sealed class M4ReportTests
{
    [Fact]
    public void EscapesUntrustedFindingAndIncludesCoverageAndTokenUsage()
    {
        string baseSha = new('a', 40);
        string headSha = new('b', 40);
        Run run = Run.Queue(Guid.NewGuid(), "owner/repo", CommitSha.Parse(baseSha),
            CommitSha.Parse(headSha), 3, DateTimeOffset.UtcNow);
        GitSnapshot snapshot = new("/tmp/workspace", CommitSha.Parse(baseSha), CommitSha.Parse(headSha),
            [new ChangedFile("demo/file.cs", "M")], "");
        M2Result analysis = new(["REQ-1"], [],
            [new AnalysisFinding("REQ-1", "demo/file.cs", 3, "high", "safe", "<script>alert(1)</script>",
                "open page", "hypothesis")], [], [], 321, 123, 1, true, null, []);
        M3Result execution = new(headSha, "/tmp/artifacts", new Dictionary<string, StageCheckResult>(),
            4, 1, new CoverageResult(new CoverageMetric(4, 8), new CoverageMetric(1, 2)),
            true, null, false, "failed");

        string report = MarkdownReportRenderer.Render(run, snapshot, analysis, execution, RunStatus.Failed,
            [], "http://127.0.0.1:5080");

        Assert.Contains("321 input, 123 output", report);
        Assert.Contains("Changed executable line coverage:** 50", report);
        Assert.Contains("&lt;script&gt;", report);
        Assert.DoesNotContain("<script>", report);
    }

    [Fact]
    public void Bot_report_shows_smoke_evidence_without_web_stand_fields()
    {
        string baseSha = new('a', 40);
        string headSha = new('b', 40);
        Run run = Run.Queue(Guid.NewGuid(), "owner/repo", CommitSha.Parse(baseSha),
            CommitSha.Parse(headSha), 11, DateTimeOffset.UtcNow);
        GitSnapshot snapshot = new("/tmp/workspace", CommitSha.Parse(baseSha), CommitSha.Parse(headSha), [], "");
        M2Result analysis = new([], [], [], [], [], 0, 0, 0, true, null, []);
        M3Result execution = new(headSha, "/tmp/artifacts",
            new Dictionary<string, StageCheckResult>
            {
                ["host-smoke"] = new(true, 0, "/tmp/artifacts/host-smoke.log", "Host /health returned HTTP 200.")
            }, 5, 0, null, false, null, null, null, M3ExecutionProfile.LeadQualification);

        string report = MarkdownReportRenderer.Render(run, snapshot, analysis, execution, RunStatus.Passed,
            [], "http://127.0.0.1:5080");

        Assert.Contains("host-smoke:** passed", report);
        Assert.DoesNotContain("**Stand:**", report);
        Assert.DoesNotContain("**Browser:**", report);
    }

    [Fact]
    public void Code_spans_are_raw_and_fenced_and_null_strings_are_tolerated()
    {
        string baseSha = new('a', 40);
        string headSha = new('b', 40);
        Run run = Run.Queue(Guid.NewGuid(), "owner/repo", CommitSha.Parse(baseSha),
            CommitSha.Parse(headSha), 3, DateTimeOffset.UtcNow);
        GitSnapshot snapshot = new("/tmp/workspace", CommitSha.Parse(baseSha), CommitSha.Parse(headSha),
            [new ChangedFile("a`b<c>.cs", "M")], "");
        M2Result analysis = new(["List<string>"], [],
            [new AnalysisFinding("REQ-1", "`x`", 3, null!, null!, null!, null!, null!)], [], [], 0, 0, 0, true, null, []);
        M3Result execution = new(headSha, "/tmp/artifacts", new Dictionary<string, StageCheckResult>(),
            4, 1, null, true, null, false, "failed");

        string report = MarkdownReportRenderer.Render(run, snapshot, analysis, execution, RunStatus.Failed,
            [], "http://127.0.0.1:5080");

        Assert.Contains("`List<string>`", report);
        Assert.Contains("``M a`b<c>.cs``", report);
        Assert.Contains("`` `x`:3 ``", report);
    }
}
