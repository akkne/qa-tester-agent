using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Domain;

namespace QaAgent.UnitTests;

public sealed class M3OutcomeTests
{
    [Fact]
    public void Healthy_full_execution_passes()
    {
        Assert.Equal(RunStatus.Passed, M3Outcome.From(Result()).Status);
    }

    [Fact]
    public void Missing_stand_or_browser_never_passes()
    {
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(Result() with { StandAvailable = false }).Status);
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(Result() with { BrowserJson = "{\"status\":\"BLOCKED\"}" }).Status);
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(Result() with { BrowserJson = null }).Status);
    }

    [Fact]
    public void Reproduced_test_or_browser_failure_fails()
    {
        M3Result testFailure = Result() with
        {
            Checks = Checks(withTestFailure: true)
        };
        Assert.Equal(RunStatus.Failed, M3Outcome.From(testFailure).Status);
        Assert.Equal(RunStatus.Failed, M3Outcome.From(Result() with
        {
            BrowserPassed = false,
            BrowserJson = "{\"status\":\"FAILED\"}"
        }).Status);
    }

    [Fact]
    public void Missing_test_or_coverage_measurement_blocks()
    {
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(Result() with { TotalTests = null }).Status);
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(Result() with
        {
            Coverage = new CoverageResult(new CoverageMetric(0, 0, "missing"), new CoverageMetric(0, 0, "missing"))
        }).Status);
    }

    private static M3Result Result() => new("a", "artifacts", Checks(), 5, 0,
        new CoverageResult(new CoverageMetric(1, 2), new CoverageMetric(0, 0, "no executable changes")),
        true, null, true, "{\"status\":\"PASSED\"}");

    private static IReadOnlyDictionary<string, StageCheckResult> Checks(bool withTestFailure = false) =>
        new Dictionary<string, StageCheckResult>
        {
            ["restore"] = new(true, 0, "", ""),
            ["format-whitespace"] = new(true, 0, "", ""),
            ["format-style"] = new(true, 0, "", ""),
            ["build"] = new(true, 0, "", ""),
            ["test"] = new(!withTestFailure, withTestFailure ? 1 : 0, "", "")
        };
}
