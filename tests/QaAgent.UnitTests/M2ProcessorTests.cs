using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Domain;

namespace QaAgent.UnitTests;

public sealed class M2ProcessorTests
{
    private const string Plan = """
        {"scenarios":[{"requirementId":"DEMO-01","kind":"positive","expected":"account created","steps":"register"},{"requirementId":"DEMO-01","kind":"negative","expected":"invalid rejected","steps":"submit invalid"},{"requirementId":"DEMO-01","kind":"access-control","expected":"private","steps":"open another account"}],"contextGaps":[]}
        """;
    private const string Findings = """
        {"findings":[{"requirementId":"DEMO-01","path":"demo/Demo.Web/Program.cs","line":12,"severity":"high","expected":"private","observed":"route may expose account","reproduction":"login as A then request B","status":"reproduced"}]}
        """;
    private const string Tests = """
        {"tests":[{"path":"demo/Demo.Tests/Generated/Positive.cs","requirementIds":["DEMO-01"],"kind":"positive","source":"[Fact] public void Positive() { Assert.True(1 == 1); }"},{"path":"demo/Demo.Tests/Generated/Negative.cs","requirementIds":["DEMO-01"],"kind":"negative","source":"[Fact] public void Negative() { Assert.True(1 == 1); }"},{"path":"demo/Demo.Tests/Generated/Access.cs","requirementIds":["DEMO-01"],"kind":"access-control","source":"[Fact] public void Access() { Assert.True(1 == 1); }"}]}
        """;

    [Fact]
    public async Task GenerationFixesAtMostTwiceAndFindingsStayHypotheses()
    {
        FakeLlm llm = new(Plan, Findings, Tests, Tests, Tests);
        FakeBuilder builder = new(false, false, true);
        M2Result result = await new M2Processor(llm, new FakeContext(), builder, new FakeScanner())
            .ProcessAsync(Snapshot(), Profile(), CancellationToken.None);

        Assert.True(result.TestsCompile);
        Assert.Equal(2, result.GenerationFixes);
        Assert.Equal(5, llm.Calls);
        Assert.Equal("hypothesis", Assert.Single(result.Findings).Status);
        Assert.Equal(3, result.Tests.Count);
    }

    [Fact]
    public async Task InvalidRequirementIdStopsBeforeTestGeneration()
    {
        FakeLlm llm = new(Plan.Replace("DEMO-01", "UNKNOWN", StringComparison.Ordinal));
        M2Processor processor = new(llm, new FakeContext(), new FakeBuilder(true), new FakeScanner());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(Snapshot(), Profile(), CancellationToken.None));
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public async Task ProductFailureDoesNotTriggerGenerationFix()
    {
        FakeLlm llm = new(Plan, Findings, Tests);
        FakeBuilder builder = new(true);
        M2Result result = await new M2Processor(llm, new FakeContext(), builder, new FakeScanner())
            .ProcessAsync(Snapshot(), Profile(), CancellationToken.None);

        Assert.Equal(0, result.GenerationFixes);
        Assert.Equal(3, llm.Calls);
    }

    [Fact]
    public async Task TechnicalChangeCanUseChangedExistingTestsWithoutGeneratingMore()
    {
        const string plan = """
            {"scenarios":[{"requirementId":"CHANGE-BBBBBBBBBBBB","kind":"positive","expected":"feature enabled","steps":"run existing composition test"}],"contextGaps":[]}
            """;
        FakeLlm llm = new(plan, """{"findings":[]}""", """{"tests":[]}""");
        FakeBuilder builder = new();
        GitSnapshot snapshot = Snapshot() with
        {
            Files = [new ChangedFile("demo/Demo.Tests/Host/CompositionTests.cs", "M")]
        };

        M2Result result = await new M2Processor(llm, new TechnicalContext(), builder, new FakeScanner())
            .ProcessAsync(snapshot, Profile(), CancellationToken.None);

        Assert.True(result.TestsCompile);
        Assert.Empty(result.Tests);
        Assert.Equal(["CHANGE-BBBBBBBBBBBB"], result.RequirementIds);
        Assert.Equal(3, llm.Calls);
        Assert.Equal(0, builder.Calls);
    }

    [Fact]
    public async Task TechnicalChangeWithoutExistingOrGeneratedTestsIsRejected()
    {
        const string plan = """
            {"scenarios":[{"requirementId":"CHANGE-BBBBBBBBBBBB","kind":"positive","expected":"feature enabled","steps":"check startup"}],"contextGaps":[]}
            """;
        FakeLlm llm = new(plan, """{"findings":[]}""", """{"tests":[]}""");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new M2Processor(llm, new TechnicalContext(), new FakeBuilder(), new FakeScanner())
                .ProcessAsync(Snapshot(), Profile(), CancellationToken.None));

        Assert.Contains("No tests were generated", error.Message);
    }

    private static GitSnapshot Snapshot() => new("/tmp/snapshot", CommitSha.Parse(new string('a', 40)),
        CommitSha.Parse(new string('b', 40)), [new ChangedFile("demo/Demo.Web/Program.cs", "M")], "diff");

    private static ProjectProfile Profile() => new("demo", 2, "demo/docs/specs", ["demo/Demo.Tests"], "revision");

    private sealed class FakeContext : IM2ContextReader
    {
        public Task<M2Context> ReadAsync(GitSnapshot snapshot, ProjectProfile profile, CancellationToken cancellationToken) =>
            Task.FromResult(new M2Context("requirement and diff", ["DEMO-01"], ["DEMO-01"], []));
    }

    private sealed class TechnicalContext : IM2ContextReader
    {
        public Task<M2Context> ReadAsync(GitSnapshot snapshot, ProjectProfile profile, CancellationToken cancellationToken) =>
            Task.FromResult(new M2Context("technical diff and changed composition test", ["UNRELATED-01"], [], []));
    }

    private sealed class FakeScanner : ISecretScanner
    {
        public Task<IReadOnlyList<SecretFinding>> ScanAsync(GitSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SecretFinding>>([]);
    }

    private sealed class FakeLlm(params string[] responses) : ILlmClient
    {
        public int Calls { get; private set; }

        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new LlmResponse(responses[Calls++], 10, 20));
    }

    private sealed class FakeBuilder(params bool[] results) : IM2TestBuilder
    {
        private int count;
        public int Calls => count;

        public Task<TestBuildResult> BuildAsync(GitSnapshot snapshot, ProjectProfile profile,
            IReadOnlyList<GeneratedTest> tests, CancellationToken cancellationToken) =>
            Task.FromResult(new TestBuildResult(results[count++], "compile error"));
    }
}
