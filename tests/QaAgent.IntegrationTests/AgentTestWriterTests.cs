using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Domain;
using QaAgent.Infrastructure.Analysis;
using QaAgent.Infrastructure.Execution;
using QaAgent.Infrastructure.Llm;

namespace QaAgent.IntegrationTests;

public sealed class AgentTestWriterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "qa-writer-" + Guid.NewGuid().ToString("N"));
    private readonly string evidence;
    private readonly string head;

    public AgentTestWriterTests()
    {
        evidence = Path.Combine(root + "-artifacts", "authoring");
        Directory.CreateDirectory(Path.Combine(root, "app"));
        Directory.CreateDirectory(Path.Combine(root, "tests"));
        Directory.CreateDirectory(Path.Combine(evidence, "commands"));
        File.WriteAllText(Path.Combine(root, "app/pricing.py"), "def price(x):\n    return x\n");
        File.WriteAllText(Path.Combine(root, "tests/test_existing.py"), "def test_existing():\n    assert True\n");
        Git("init", "-q");
        Git("add", ".");
        Git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "init");
        head = Git("rev-parse", "HEAD").Trim();
    }

    [Fact]
    public async Task AcceptsNewTestsInAnyLanguageWhenTheirBuildPassed()
    {
        File.WriteAllText(Path.Combine(root, "tests/test_pricing.py"),
            "from app.pricing import price\n\ndef test_price():\n    assert price(2) == 2\n");
        File.WriteAllText(Path.Combine(root, "tests/conftest_extra.py"), "VALUE = 1\n");
        File.WriteAllText(Path.Combine(root, "tests/test_rejects.py"),
            "import pytest\nfrom app.pricing import price\n\ndef test_rejects():\n    with pytest.raises(ValueError):\n        price(-1)\n");
        File.WriteAllText(Path.Combine(root, "scratch.txt"), "build output");
        Ledger(("api-pytest", "build", 0));

        TestWritingResult result = await Collect("""
            {"status":"completed","blockReason":null,"files":[
              {"path":"tests/test_pricing.py","kind":"positive","requirementIds":["REQ-01"],"suite":"api-pytest"},
              {"path":"/src/tests/conftest_extra.py","kind":"support","requirementIds":[],"suite":"api-pytest"},
              {"path":"tests/test_rejects.py","kind":"negative","requirementIds":["REQ-01"],"suite":"api-pytest"}],"notes":"ok"}
            """);

        Assert.True(result.Build.Compiles, result.Build.Diagnostics);
        Assert.Equal(["tests/conftest_extra.py", "tests/test_pricing.py", "tests/test_rejects.py"],
            result.Tests.Select(test => test.Path).Order());
        Assert.Contains("assert price(2) == 2", result.Tests.Single(test => test.Kind == "positive").Source);
        Assert.True(File.Exists(Path.Combine(root, "tests/test_pricing.py")));
        Assert.False(File.Exists(Path.Combine(root, "scratch.txt")));
    }

    [Fact]
    public async Task DiscardsForbiddenSkippedAndAssertionFreeFiles()
    {
        File.WriteAllText(Path.Combine(root, "app/test_outside.py"), "def test_x():\n    assert 1\n");
        File.WriteAllText(Path.Combine(root, "tests/test_skip.py"),
            "import pytest\n\n@pytest.mark.skip\ndef test_x():\n    assert 1\n");
        File.WriteAllText(Path.Combine(root, "tests/test_empty.py"), "def test_x():\n    pass\n");
        File.WriteAllText(Path.Combine(root, "tests/test_unknown.py"), "def test_x():\n    assert 1\n");
        Ledger(("api-pytest", "build", 0));

        TestWritingResult result = await Collect("""
            {"status":"completed","blockReason":null,"files":[
              {"path":"app/test_outside.py","kind":"positive","requirementIds":["REQ-01"],"suite":"api-pytest"},
              {"path":"tests/test_skip.py","kind":"positive","requirementIds":["REQ-01"],"suite":"api-pytest"},
              {"path":"tests/test_empty.py","kind":"negative","requirementIds":["REQ-01"],"suite":"api-pytest"},
              {"path":"tests/test_unknown.py","kind":"negative","requirementIds":["OTHER-9"],"suite":"api-pytest"},
              {"path":"tests/test_existing.py","kind":"negative","requirementIds":["REQ-01"],"suite":"api-pytest"}],"notes":""}
            """);

        Assert.Empty(result.Tests);
        Assert.False(result.Build.Compiles);
        Assert.Equal(5, result.Gaps.Count(gap => gap.Contains("was discarded", StringComparison.Ordinal)));
        Assert.False(File.Exists(Path.Combine(root, "tests/test_skip.py")));
    }

    [Fact]
    public async Task CSharpTestsGoThroughTheSameChecks()
    {
        File.WriteAllText(Path.Combine(root, "tests/PriceTests.cs"),
            "public class PriceTests { [Fact] public void P() { Assert.Equal(2, Price.Of(2)); } }");
        File.WriteAllText(Path.Combine(root, "tests/SkippedTests.cs"),
            "public class SkippedTests { [Fact(Skip = \"later\")] public void S() { Assert.True(false); } }");
        Ledger(("core-dotnet", "build", 1), ("core-dotnet", "build", 0));

        TestWritingResult result = await Collect("""
            {"status":"completed","blockReason":null,"files":[
              {"path":"tests/PriceTests.cs","kind":"positive","requirementIds":["REQ-01"],"suite":"core-dotnet"},
              {"path":"tests/SkippedTests.cs","kind":"negative","requirementIds":["REQ-01"],"suite":"core-dotnet"}],"notes":""}
            """);

        Assert.True(result.Build.Compiles, result.Build.Diagnostics);
        Assert.Equal("tests/PriceTests.cs", Assert.Single(result.Tests).Path);
        Assert.Equal(1, result.Fixes);
        Assert.Contains(result.Gaps, gap => gap.Contains("SkippedTests.cs was discarded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FileUnderSymlinkedDirectoryIsDiscarded()
    {
        string outside = root + "-outside";
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "test_evil.py"), "def test_x():\n    assert 1\n");
            Directory.CreateSymbolicLink(Path.Combine(root, "tests/linked"), outside);
            Ledger(("api-pytest", "build", 0));

            TestWritingResult result = await Collect("""
                {"status":"completed","blockReason":null,"files":[
                  {"path":"tests/linked/test_evil.py","kind":"positive","requirementIds":["REQ-01"],"suite":"api-pytest"}],"notes":""}
                """);

            Assert.Empty(result.Tests);
            Assert.True(File.Exists(Path.Combine(outside, "test_evil.py")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task FailedOrMissingBuildMarksTestsAsNotCompiling()
    {
        File.WriteAllText(Path.Combine(root, "tests/test_a.py"), "def test_a():\n    assert 1\n");
        File.WriteAllText(Path.Combine(root, "tests/b.test.ts"), "test('b', () => { expect(1).toBe(1); });\n");
        Ledger(("api-pytest", "build", 0), ("api-pytest", "build", 2));

        TestWritingResult result = await Collect("""
            {"status":"completed","blockReason":null,"files":[
              {"path":"tests/test_a.py","kind":"positive","requirementIds":["REQ-01"],"suite":"api-pytest"},
              {"path":"tests/b.test.ts","kind":"negative","requirementIds":["REQ-01"],"suite":"web-vitest"}],"notes":""}
            """);

        Assert.Equal(2, result.Tests.Count);
        Assert.False(result.Build.Compiles);
        Assert.Contains("'api-pytest': build exited 2", result.Build.Diagnostics);
        Assert.Contains("'web-vitest': the agent never compiled", result.Build.Diagnostics);
    }

    [Fact]
    public async Task ModifiedTrackedFileRejectsEverything()
    {
        File.WriteAllText(Path.Combine(root, "app/pricing.py"), "def price(x):\n    return 2\n");
        File.WriteAllText(Path.Combine(root, "tests/test_a.py"), "def test_a():\n    assert 1\n");
        Ledger(("api-pytest", "build", 0));

        TestWritingResult result = await Collect("""
            {"status":"completed","blockReason":null,"files":[
              {"path":"tests/test_a.py","kind":"positive","requirementIds":["REQ-01"],"suite":"api-pytest"}],"notes":""}
            """);

        Assert.Empty(result.Tests);
        Assert.Contains("modified tracked files", result.Build.Diagnostics);
        Assert.Equal("def price(x):\n    return x\n", File.ReadAllText(Path.Combine(root, "app/pricing.py")));
        Assert.False(File.Exists(Path.Combine(root, "tests/test_a.py")));
    }

    [Fact]
    public void PruneKeepsOnlyEvidenceAndDoesNotFollowLinks()
    {
        string outside = root + "-outside";
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "x");
            Directory.CreateDirectory(Path.Combine(evidence, "dotnet/sdk"));
            File.WriteAllText(Path.Combine(evidence, "dotnet/sdk/big.dll"), "x");
            File.WriteAllText(Path.Combine(evidence, "ledger.jsonl"), "");
            Directory.CreateSymbolicLink(Path.Combine(evidence, "link"), outside);

            CodexContainer.PruneEvidence(evidence, "commands", "ledger.jsonl");

            Assert.Equal(["commands", "ledger.jsonl"],
                Directory.EnumerateFileSystemEntries(evidence).Select(Path.GetFileName).Order());
            Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    private Task<TestWritingResult> Collect(string answer)
    {
        AgentTestWriter writer = new(root + "-artifacts", new AgentExecutionOptions(),
            new FileCodexActivityStore(Path.Combine(root + "-artifacts", "activity")), NullLogger<AgentTestWriter>.Instance);
        TestWritingRequest request = new(
            new GitSnapshot(root, CommitSha.Parse(head), CommitSha.Parse(head), [], "diff"),
            new ProjectProfile("demo", 1, "docs", ["tests"], "revision"),
            ["REQ-01"], true, "context", "{}", "{}", Guid.NewGuid());
        return writer.CollectAsync(request, root, evidence, new CodexContainerResult(answer, null, 1, 2),
            CancellationToken.None);
    }

    private void Ledger(params (string Suite, string Phase, int ExitCode)[] records)
    {
        File.WriteAllLines(Path.Combine(evidence, "ledger.jsonl"), records.Select((record, index) =>
            $$"""{"seq":{{index + 1}},"suite":"{{record.Suite}}","phase":"{{record.Phase}}","cwd":"/src","command":"build","exitCode":{{record.ExitCode}},"durationMs":1,"log":"commands/{{index + 1}}.log"}"""));
        for (int index = 0; index < records.Length; index++)
            File.WriteAllText(Path.Combine(evidence, "commands", $"{index + 1}.log"), "SyntaxError in test");
    }

    private string Git(params string[] arguments)
    {
        ProcessStartInfo start = new("git") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(root);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException(process.StandardError.ReadToEnd());
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        Directory.Delete(root + "-artifacts", recursive: true);
    }
}
