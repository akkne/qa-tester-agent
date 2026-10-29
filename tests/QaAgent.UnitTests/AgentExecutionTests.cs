using System.Text.Json;
using QaAgent.Application.Execution;
using QaAgent.Domain;

namespace QaAgent.UnitTests;

public sealed class AgentExecutionTests
{
    [Fact]
    public void Verified_passing_suites_pass()
    {
        AgentExecution agent = Evaluate(Report(Suite("api")), [Setup("api"), Test("api", 0)], Reports("api", total: 3));

        Assert.Equal(SuiteVerdict.Passed, agent.Suites.Single().Verdict);
        Assert.Equal(3, agent.Suites.Single().Total);
        Assert.Equal(RunStatus.Passed, Outcome(agent));
    }

    [Fact]
    public void Failed_test_in_report_fails_even_when_exit_code_is_zero()
    {
        AgentExecution agent = Evaluate(Report(Suite("api")), [Test("api", 0)], Reports("api", total: 3, failed: 1));

        Assert.Equal(SuiteVerdict.Failed, agent.Suites.Single().Verdict);
        Assert.Equal(RunStatus.Failed, Outcome(agent));
    }

    [Fact]
    public void Nonzero_test_exit_fails_even_when_report_is_green()
    {
        AgentExecution agent = Evaluate(Report(Suite("api")), [Test("api", 1)], Reports("api", total: 3));

        Assert.Equal(SuiteVerdict.Failed, agent.Suites.Single().Verdict);
    }

    [Fact]
    public void Compile_error_fails_but_dependency_install_failure_blocks()
    {
        Assert.Equal(RunStatus.Failed, Outcome(Evaluate(Report(Suite("api")),
            [Setup("api"), new CommandRecord(2, "api", "build", 1, "b.log", "make")], Empty)));
        Assert.Equal(RunStatus.Blocked, Outcome(Evaluate(Report(Suite("api")),
            [new CommandRecord(1, "api", "setup", 1, "s.log", "npm ci")], Empty)));
    }

    [Theory]
    [InlineData(124)]
    [InlineData(127)]
    [InlineData(137)]
    public void Environment_exit_codes_block_instead_of_failing(int exitCode)
    {
        AgentExecution agent = Evaluate(Report(Suite("api")), [Test("api", exitCode)], Empty);

        Assert.Equal(SuiteVerdict.Blocked, agent.Suites.Single().Verdict);
        Assert.Equal(RunStatus.Blocked, Outcome(agent));
    }

    [Fact]
    public void Rerun_that_turns_green_is_flaky_and_needs_review()
    {
        AgentExecution agent = Evaluate(Report(Suite("api")),
            [Test("api", 1, sequence: 1), Test("api", 0, sequence: 2)], Reports("api", total: 3));

        Assert.Equal(SuiteVerdict.Flaky, agent.Suites.Single().Verdict);
        Assert.Equal(RunStatus.NeedsReview, Outcome(agent));
    }

    [Fact]
    public void Green_suite_without_report_is_unverified_and_counts_stay_agent_reported()
    {
        AgentExecution agent = Evaluate(Report(Suite("api") with { Counts = new AgentCounts(9, 0, 0) }),
            [Test("api", 0)], Empty);

        SuiteResult suite = agent.Suites.Single();
        Assert.Equal(SuiteVerdict.Unverified, suite.Verdict);
        Assert.False(suite.CountsVerified);
        Assert.Equal(9, suite.Total);
        Assert.Equal(RunStatus.NeedsReview, Outcome(agent));
    }

    [Fact]
    public void Report_with_zero_tests_blocks()
    {
        AgentExecution agent = Evaluate(Report(Suite("api")), [Test("api", 0)], Reports("api", total: 0));

        Assert.Equal(SuiteVerdict.Blocked, agent.Suites.Single().Verdict);
    }

    [Fact]
    public void Failing_suite_omitted_from_the_report_is_still_judged()
    {
        AgentExecution agent = Evaluate(Report(Suite("api")),
            [Test("api", 0), Test("web", 1, sequence: 2)], Reports("api", total: 2));

        Assert.Contains(agent.Suites, suite => suite.Id == "web" && suite.Verdict == SuiteVerdict.Failed);
        Assert.Contains(agent.Warnings, warning => warning.Contains("'web'", StringComparison.Ordinal));
        Assert.Equal(RunStatus.Failed, Outcome(agent));
    }

    [Fact]
    public void Declared_suite_without_records_blocks()
    {
        AgentExecution agent = Evaluate(Report(Suite("api")), [], Empty);

        Assert.Equal(SuiteVerdict.Blocked, agent.Suites.Single().Verdict);
        Assert.Equal(RunStatus.Blocked, Outcome(agent));
    }

    [Fact]
    public void Skipped_suite_needs_review()
    {
        AgentExecution agent = Evaluate(Report(Suite("api")) with { NotRun = [new("e2e", "needs a Stripe key")] },
            [Test("api", 0)], Reports("api", total: 1));

        Assert.Equal(RunStatus.NeedsReview, Outcome(agent));
    }

    [Fact]
    public void Modified_tracked_files_missing_report_or_no_suites_block()
    {
        Assert.Equal(RunStatus.Blocked, Outcome(AgentExecution.Evaluate(Report(Suite("api")), [Test("api", 0)],
            Reports("api", total: 1), ["package-lock.json"], 0, 0)));
        Assert.Equal(RunStatus.Blocked, Outcome(Evaluate(null, [Test("api", 0)], Reports("api", total: 1))));
        Assert.Equal(RunStatus.Blocked, Outcome(Evaluate(Report(), [], Empty)));
        Assert.Equal(RunStatus.Blocked, M3Outcome.From(Execution(null)).Status);
    }

    [Fact]
    public void Count_mismatch_with_the_agent_claim_is_a_warning()
    {
        AgentExecution agent = Evaluate(Report(Suite("api") with { Counts = new AgentCounts(10, 0, 0) }),
            [Test("api", 0)], Reports("api", total: 3));

        Assert.Equal(SuiteVerdict.Passed, agent.Suites.Single().Verdict);
        Assert.Contains(agent.Warnings, warning => warning.Contains("10 total", StringComparison.Ordinal));
    }

    [Fact]
    public void Schema_requires_every_property_for_strict_structured_output()
    {
        using JsonDocument schema = JsonDocument.Parse(AgentTestContract.Schema);
        AssertStrict(schema.RootElement);
        Assert.Contains("qa-run --suite", AgentTestContract.Prompt(["Use make test"], false, 600), StringComparison.Ordinal);
        Assert.Contains("- Use make test", AgentTestContract.Prompt(["Use make test"], false, 600), StringComparison.Ordinal);

        static void AssertStrict(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("properties", out JsonElement properties))
                {
                    string[] names = properties.EnumerateObject().Select(property => property.Name).Order().ToArray();
                    string[] required = node.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).Order().ToArray();
                    Assert.Equal(names, required);
                    Assert.False(node.GetProperty("additionalProperties").GetBoolean());
                }

                foreach (JsonProperty property in node.EnumerateObject()) AssertStrict(property.Value);
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in node.EnumerateArray()) AssertStrict(item);
            }
        }
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<ParsedTestReport>> Empty =
        new Dictionary<string, IReadOnlyList<ParsedTestReport>>();

    private static AgentExecution Evaluate(AgentTestReport? report, IReadOnlyList<CommandRecord> ledger,
        IReadOnlyDictionary<string, IReadOnlyList<ParsedTestReport>> reports) =>
        AgentExecution.Evaluate(report, ledger, reports, [], 10, 5);

    private static RunStatus Outcome(AgentExecution agent) => M3Outcome.From(Execution(agent)).Status;

    private static M3Result Execution(AgentExecution? agent) => new("a", "artifacts",
        new Dictionary<string, StageCheckResult>(), null, null, null, false, null, null, null,
        M3ExecutionProfile.Agent, agent);

    private static AgentTestReport Report(params AgentSuite[] suites) => new("completed", null, suites, []);

    private static AgentSuite Suite(string id) => new(id, "python", "pytest", ".", [], [], null, "", "");

    private static CommandRecord Setup(string suite) => new(0, suite, "setup", 0, "setup.log", "pip install");

    private static CommandRecord Test(string suite, int exitCode, int sequence = 1) =>
        new(sequence, suite, "test", exitCode, "test.log", "pytest");

    private static Dictionary<string, IReadOnlyList<ParsedTestReport>> Reports(string suite, int total, int failed = 0) =>
        new() { [suite] = [new ParsedTestReport("junit.xml", total, failed, 0, Enumerable.Repeat("t", failed).ToArray())] };
}
