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
    private const string TechnicalPlan = """
        {"scenarios":[{"requirementId":"CHANGE-BBBBBBBBBBBB","kind":"positive","expected":"feature enabled","steps":"check startup"}],"contextGaps":[]}
        """;

    private static readonly GeneratedTest[] AllKinds =
    [
        new("demo/Demo.Tests/Generated/PositiveTests.cs", ["DEMO-01"], "positive", "[Fact] public void P() { Assert.True(ok); }"),
        new("demo/Demo.Tests/Generated/NegativeTests.cs", ["DEMO-01"], "negative", "[Fact] public void N() { Assert.False(ok); }"),
        new("demo/Demo.Tests/Generated/AccessTests.cs", ["DEMO-01"], "access-control", "[Fact] public void A() { Assert.False(ok); }")
    ];

    [Fact]
    public async Task WriterProducesTestsAndFindingsStayHypotheses()
    {
        FakeLlm llm = new(Plan, Findings);
        FakeWriter writer = new(AllKinds, true, fixes: 2);

        M2Result result = await new M2Processor(llm, new FakeContext(), writer, new FakeScanner())
            .ProcessAsync(Snapshot(), Profile(), CancellationToken.None);

        Assert.True(result.TestsCompile);
        Assert.Equal(3, result.Tests.Count);
        Assert.Equal(2, result.GenerationFixes);
        Assert.Equal(2, llm.Calls);
        Assert.Equal((2 * 10) + 7, result.InputTokens);
        Assert.Equal("hypothesis", Assert.Single(result.Findings).Status);
        Assert.Equal(["DEMO-01"], writer.Request!.RequirementIds);
        Assert.True(writer.Request.SpecificationChanged);
        Assert.Contains("\"access-control\"", writer.Request.PlanJson);
    }

    [Fact]
    public async Task InvalidRequirementIdStopsBeforeTestWriting()
    {
        FakeLlm llm = new(Plan.Replace("DEMO-01", "UNKNOWN", StringComparison.Ordinal));
        FakeWriter writer = new(AllKinds, true);
        M2Processor processor = new(llm, new FakeContext(), writer, new FakeScanner());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(Snapshot(), Profile(), CancellationToken.None));
        Assert.Equal(1, llm.Calls);
        Assert.Null(writer.Request);
    }

    [Fact]
    public async Task FailedWriterBuildIsReportedWithItsDiagnostics()
    {
        M2Result result = await new M2Processor(new FakeLlm(Plan, Findings), new FakeContext(),
            new FakeWriter(AllKinds, false), new FakeScanner()).ProcessAsync(Snapshot(), Profile(), CancellationToken.None);

        Assert.False(result.TestsCompile);
        Assert.Equal("build failed", result.BuildDiagnostics);
    }

    [Fact]
    public async Task MissingKindDropsTestsButKeepsPlanAndFindings()
    {
        FakeWriter writer = new([AllKinds[0]], true);

        M2Result result = await new M2Processor(new FakeLlm(Plan, Findings), new FakeContext(), writer, new FakeScanner())
            .ProcessAsync(Snapshot(), Profile(), CancellationToken.None);

        Assert.False(result.TestsCompile);
        Assert.Contains("positive, negative, and access-control", result.BuildDiagnostics);
        Assert.Equal(3, result.Plan.Count);
        Assert.Single(result.Findings);
    }

    [Fact]
    public async Task UnknownRequirementIdFromWriterIsRejected()
    {
        GeneratedTest[] tests = [.. AllKinds, new("demo/Demo.Tests/Generated/Other.cs", ["OTHER-9"], "boundary", "Assert.True(x);")];

        M2Result result = await new M2Processor(new FakeLlm(Plan, Findings), new FakeContext(),
            new FakeWriter(tests, true), new FakeScanner()).ProcessAsync(Snapshot(), Profile(), CancellationToken.None);

        Assert.False(result.TestsCompile);
        Assert.Contains("OTHER-9", result.BuildDiagnostics);
    }

    [Fact]
    public async Task TechnicalChangeCanUseChangedExistingTestsOfAnyLanguage()
    {
        GitSnapshot snapshot = Snapshot() with { Files = [new ChangedFile("demo/Demo.Tests/test_feature.py", "M")] };

        M2Result result = await new M2Processor(new FakeLlm(TechnicalPlan, """{"findings":[]}"""), new TechnicalContext(),
            new FakeWriter([], true), new FakeScanner()).ProcessAsync(snapshot, Profile(), CancellationToken.None);

        Assert.True(result.TestsCompile);
        Assert.Empty(result.Tests);
        Assert.Equal(["CHANGE-BBBBBBBBBBBB"], result.RequirementIds);
    }

    [Fact]
    public async Task TechnicalChangeWithoutExistingOrWrittenTestsIsReported()
    {
        M2Result result = await new M2Processor(new FakeLlm(TechnicalPlan, """{"findings":[]}"""), new TechnicalContext(),
            new FakeWriter([], true), new FakeScanner()).ProcessAsync(Snapshot(), Profile(), CancellationToken.None);

        Assert.False(result.TestsCompile);
        Assert.Contains("No tests were generated", result.BuildDiagnostics);
        Assert.Single(result.Plan);
    }

    [Theory]
    [InlineData("demo/Demo.Tests/test_a.py", true)]
    [InlineData("demo/Demo.Tests/Sub/A.cs", true)]
    [InlineData("demo/Demo.Testsx/A.cs", false)]
    [InlineData("demo/Demo.Tests/../Web/A.cs", false)]
    [InlineData("/demo/Demo.Tests/A.cs", false)]
    [InlineData("demo/Demo.Web/a_test.go", false)]
    public void ProfileAllowsOnlyPathsInsideTestDirectories(string path, bool allowed) =>
        Assert.Equal(allowed, Profile().AllowsTestPath(path));

    [Fact]
    public void RootTestDirectoryAllowsTestsNextToTheCode()
    {
        ProjectProfile profile = Profile() with { AllowedTestDirectories = ["."] };

        Assert.True(profile.AllowsTestPath("pkg/pricing/price_test.go"));
        Assert.False(profile.AllowsTestPath("../outside_test.go"));
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

    private sealed class FakeWriter(IReadOnlyList<GeneratedTest> tests, bool compiles, int fixes = 0) : IM2TestWriter
    {
        public TestWritingRequest? Request { get; private set; }

        public Task<TestWritingResult> WriteAsync(TestWritingRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new TestWritingResult(tests, new TestBuildResult(compiles, compiles ? "" : "build failed"),
                [], 7, 9, fixes));
        }
    }
}
