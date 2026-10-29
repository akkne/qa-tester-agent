using QaAgent.Application.Execution;
using QaAgent.Infrastructure.Execution;

namespace QaAgent.IntegrationTests;

public sealed class TestReportParserTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("qa-agent-reports-").FullName;

    [Fact]
    public void Counts_junit_test_cases_including_nested_suites()
    {
        string path = Write("junit.xml", """
            <testsuites tests="99">
              <testsuite name="a">
                <testcase classname="pkg.A" name="ok"/>
                <testcase classname="pkg.A" name="broken"><failure message="x"/></testcase>
                <testsuite name="nested"><testcase name="crash"><error/></testcase></testsuite>
                <testcase classname="pkg.A" name="later"><skipped/></testcase>
              </testsuite>
            </testsuites>
            """);

        ParsedTestReport report = TestReportParser.Parse(path);

        Assert.Null(report.Error);
        Assert.Equal(4, report.Total);
        Assert.Equal(2, report.Failed);
        Assert.Equal(1, report.Skipped);
        Assert.Equal(["pkg.A.broken", "crash"], report.FailedTests);
    }

    [Fact]
    public void Counts_trx_outcomes()
    {
        string path = Write("run.trx", """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>
              <UnitTestResult testName="A" outcome="Passed"/>
              <UnitTestResult testName="B" outcome="Failed"/>
              <UnitTestResult testName="C" outcome="NotExecuted"/>
            </Results></TestRun>
            """);

        ParsedTestReport report = TestReportParser.Parse(path);

        Assert.Equal((3, 1, 1), (report.Total, report.Failed, report.Skipped));
        Assert.Equal(["B"], report.FailedTests);
    }

    [Fact]
    public void Rejects_unknown_malformed_and_dtd_reports()
    {
        Assert.NotNull(TestReportParser.Parse(Write("other.xml", "<coverage/>")).Error);
        Assert.NotNull(TestReportParser.Parse(Write("bad.xml", "<testsuite>")).Error);
        Assert.NotNull(TestReportParser.Parse(Write("dtd.xml",
            "<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><testsuite><testcase name=\"&e;\"/></testsuite>")).Error);
        Assert.NotNull(TestReportParser.Parse(Path.Combine(directory, "missing.xml")).Error);
    }

    [Fact]
    public void Maps_only_paths_inside_the_artifacts_directory()
    {
        string root = Path.GetFullPath(directory);
        Assert.Equal(Path.Combine(root, "reports", "a", "junit.xml"),
            TestReportParser.MapArtifact(directory, "/artifacts/reports/a/junit.xml"));
        Assert.Equal(Path.Combine(root, "commands", "1.log"), TestReportParser.MapArtifact(directory, "commands/1.log"));
        Assert.Null(TestReportParser.MapArtifact(directory, "/artifacts/../etc/passwd"));
        Assert.Null(TestReportParser.MapArtifact(directory, "/src/junit.xml"));
        Assert.Null(TestReportParser.MapArtifact(directory, "../outside.xml"));
    }

    [Fact]
    public void Reads_ledger_and_skips_invalid_lines()
    {
        string ledger = Write("ledger.jsonl", """
            {"seq":1,"suite":"api","phase":"setup","cwd":"/src","command":"npm ci","exitCode":0,"durationMs":5,"log":"commands/0001-api-setup.log"}
            not json
            {"seq":2,"suite":"api","phase":"deploy","cwd":"/src","command":"x","exitCode":0,"durationMs":5,"log":"commands/0002.log"}
            {"seq":3,"suite":"api","phase":"test","cwd":"/src","command":"npm test","exitCode":1,"durationMs":5,"log":"commands/0003-api-test.log"}
            """);
        List<string> warnings = [];

        IReadOnlyList<CommandRecord> records = TestReportParser.ReadLedger(ledger, directory, warnings);

        Assert.Equal([1, 3], records.Select(record => record.Sequence));
        Assert.Equal(1, records[1].ExitCode);
        Assert.EndsWith(Path.Combine("commands", "0003-api-test.log"), records[1].LogPath, StringComparison.Ordinal);
        Assert.Equal(2, warnings.Count);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string Write(string name, string content)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
