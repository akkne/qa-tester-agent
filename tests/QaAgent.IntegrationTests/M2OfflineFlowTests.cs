using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Domain;
using QaAgent.Infrastructure.Analysis;

namespace QaAgent.IntegrationTests;

public sealed class M2OfflineFlowTests
{
    [Fact]
    public async Task WrittenCSharpTestsRunWithTheDemoSuite()
    {
        string repo = FindRepository();
        string clone = Path.Combine(Path.GetTempPath(), "qa-m2-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            await RunAsync(repo, "git", "clone", "--quiet", "--no-hardlinks", repo, clone);
            string head = (await RunAsync(clone, "git", "rev-parse", "HEAD")).Trim();
            string featureCommit = (await RunAsync(clone, "git", "log", "-1", "--format=%H", "--", "demo/Demo.Web")).Trim();
            string baseSha = (await RunAsync(clone, "git", "rev-parse", featureCommit + "^")).Trim();
            GitSnapshot snapshot = new(clone, CommitSha.Parse(baseSha), CommitSha.Parse(head),
                [new ChangedFile("demo/Demo.Web/Program.cs", "M")],
                await RunAsync(clone, "git", "diff", baseSha, head, "--", "demo/Demo.Web"));
            ProjectProfile profile = new("demo", 2, "demo/docs/specs", ["demo/Demo.Tests"], "revision");
            string traceId = "CHANGE-" + head[..12].ToUpperInvariant();
            M2Result result = await new M2Processor(new FakeLlm(traceId), new M2ContextReader(),
                    new FakeWriter(traceId), new DiffSecretScanner())
                .ProcessAsync(snapshot, profile, CancellationToken.None);

            Assert.True(result.TestsCompile, result.BuildDiagnostics);
            Assert.Equal(3, result.Tests.Count);
            string output = await RunAsync(clone, "dotnet", "test", "demo/Demo.Tests/Demo.Tests.csproj", "-v:q");
            Assert.Contains("Passed", output);
        }
        finally
        {
            if (Directory.Exists(clone))
            {
                Directory.Delete(clone, recursive: true);
            }
        }
    }

    private static string FindRepository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QaAgent.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution root not found.");
    }

    private static async Task<string> RunAsync(string directory, string file, params string[] arguments)
    {
        ProcessStartInfo start = new(file)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {file}.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string text = await output;
        string stderr = await error;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{file} failed: {stderr}\n{text}");
        }

        return text;
    }

    private sealed class FakeLlm(string traceId) : ILlmClient
    {
        private int call;

        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            string json = call++ switch
            {
                0 => JsonSerializer.Serialize(new
                {
                    scenarios = new[]
                    {
                        new { requirementId = traceId, kind = "positive", expected = "account created", steps = "register valid account" },
                        new { requirementId = traceId, kind = "negative", expected = "duplicate rejected", steps = "register twice" },
                        new { requirementId = traceId, kind = "access-control", expected = "anonymous denied", steps = "request private API" }
                    },
                    contextGaps = Array.Empty<string>()
                }),
                1 => "{\"findings\":[]}",
                _ => throw new InvalidOperationException("Unexpected LLM call.")
            };
            return Task.FromResult(new LlmResponse(json, 20, 30));
        }
    }

    // Stands in for the Codex test-writing agent: writes C# files into the checkout the way the agent leaves them.
    private sealed class FakeWriter(string traceId) : IM2TestWriter
    {
        public async Task<TestWritingResult> WriteAsync(TestWritingRequest request, CancellationToken cancellationToken)
        {
            GeneratedTest[] tests =
            [
                Test("Positive", "positive", "Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);",
                    "await client.PostAsJsonAsync(\"/api/register\", new RegistrationInput(\"new@example.com\", \"secure-password-123\"))"),
                Test("Negative", "negative", "Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);",
                    "await client.PostAsJsonAsync(\"/api/register\", new RegistrationInput(\"bad\", \"secure-password-123\"))"),
                Test("Access", "access-control", "Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);",
                    "await client.GetAsync(\"/api/me\")")
            ];
            foreach (GeneratedTest test in tests)
            {
                string path = Path.Combine(request.Snapshot.WorkspacePath, test.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, test.Source, cancellationToken);
            }

            return new TestWritingResult(tests, new TestBuildResult(true, ""), [], 0, 0);
        }

        private GeneratedTest Test(string name, string kind, string assertion, string httpRequest) => new(
            $"demo/Demo.Tests/Generated/{name}.cs", [traceId], kind, $$"""
                using System.Net.Http.Json;
                using Demo.Web;
                namespace Demo.Tests;
                public sealed class Generated{{name}}Test
                {
                    [Fact]
                    public async Task Requirement()
                    {
                        using DemoWebFactory factory = new();
                        using HttpClient client = factory.CreateClient();
                        HttpResponseMessage response = {{httpRequest}};
                        {{assertion}}
                    }
                }
                """);
    }
}
