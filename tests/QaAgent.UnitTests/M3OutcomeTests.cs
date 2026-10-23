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

    [Fact]
    public void Bot_passes_with_successful_offline_host_smoke_without_web_or_coverage()
    {
        M3Outcome outcome = M3Outcome.From(BotResult());
        Assert.Equal(RunStatus.Passed, outcome.Status);
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("host-smoke")]
    public void Bot_requires_every_check_to_run_and_succeed(string name)
    {
        M3Result result = BotResult();
        Dictionary<string, StageCheckResult> checks = new(result.Checks);
        checks.Remove(name);
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(result with { Checks = checks }).Status);
        checks[name] = new StageCheckResult(false, 1, name + ".log", "failed");
        // Restore depends on NuGet availability, so only build, test and smoke failures judge the code.
        Assert.Equal(name == "restore" ? RunStatus.Blocked : RunStatus.Failed,
            M3Outcome.From(result with { Checks = checks }).Status);
        checks[name] = new StageCheckResult(false, 125, name + ".log", "did not run");
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(result with { Checks = checks }).Status);
        checks[name] = new StageCheckResult(true, 0, "", "missing log");
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(result with { Checks = checks }).Status);
    }

    [Fact]
    public void Bot_requires_measured_passing_tests()
    {
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(BotResult() with { TotalTests = null }).Status);
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(BotResult() with { FailedTests = null }).Status);
        Assert.Equal(RunStatus.Failed, M3Outcome.From(BotResult() with { FailedTests = 1 }).Status);
    }

    [Fact]
    public void Bot_build_failure_is_a_failed_verdict_even_without_test_counts()
    {
        M3Result result = BotResult();
        Dictionary<string, StageCheckResult> checks = new(result.Checks)
        {
            ["build"] = new(false, 1, "build.log", "error IDE0011"),
            ["test"] = new(false, 1, "test.log", "Exec format error")
        };

        M3Outcome outcome = M3Outcome.From(result with { Checks = checks, TotalTests = null, FailedTests = null });

        Assert.Equal(RunStatus.Failed, outcome.Status);
    }

    private static M3Result Result() => new("a", "artifacts", Checks(), 5, 0,
        new CoverageResult(new CoverageMetric(1, 2), new CoverageMetric(0, 0, "no executable changes")),
        true, null, true, "{\"status\":\"PASSED\"}");

    private static M3Result BotResult()
    {
        Dictionary<string, StageCheckResult> checks = new(Checks())
        {
            ["host-smoke"] = new(true, 0, "host-smoke.log", "Host /health returned HTTP 200.")
        };
        foreach (string name in checks.Keys.ToArray())
            checks[name] = checks[name] with { LogPath = name + ".log" };
        return Result() with
        {
            Checks = checks,
            Coverage = null,
            StandAvailable = false,
            BrowserPassed = null,
            BrowserJson = null,
            Profile = M3ExecutionProfile.LeadQualification
        };
    }

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
