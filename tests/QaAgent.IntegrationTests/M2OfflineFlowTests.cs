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
    public async Task FakeLlmProducesCompilablePositiveNegativeAndAccessTests()
    {
        string repo = FindRepository();
        string clone = Path.Combine(Path.GetTempPath(), "qa-m2-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            await RunAsync(repo, "git", "clone", "--quiet", "--local", repo, clone);
            string head = (await RunAsync(clone, "git", "rev-parse", "HEAD")).Trim();
            string featureCommit = (await RunAsync(clone, "git", "log", "-1", "--format=%H", "--", "demo/Demo.Web")).Trim();
            string baseSha = (await RunAsync(clone, "git", "rev-parse", featureCommit + "^")).Trim();
            GitSnapshot snapshot = new(clone, CommitSha.Parse(baseSha), CommitSha.Parse(head),
                [new ChangedFile("demo/Demo.Web/Program.cs", "M")],
                await RunAsync(clone, "git", "diff", baseSha, head, "--", "demo/Demo.Web"));
            ProjectProfile profile = new("demo", 2, "demo/docs/specs", ["demo/Demo.Tests"], "revision");
            M2Result result = await new M2Processor(new FakeLlm(), new M2ContextReader(), new M2TestBuilder(), new DiffSecretScanner())
                .ProcessAsync(snapshot, profile, CancellationToken.None);

            Assert.True(result.TestsCompile, result.BuildDiagnostics);
            Assert.Equal(3, result.Tests.Count);
            string output = await RunAsync(clone, "dotnet", "test", "demo/Demo.Tests/Demo.Tests.csproj", "--no-restore", "-v:q");
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

    private sealed class FakeLlm : ILlmClient
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
                        new { requirementId = "DEMO-REG-01", kind = "positive", expected = "account created", steps = "register valid account" },
                        new { requirementId = "DEMO-REG-02", kind = "negative", expected = "duplicate rejected", steps = "register twice" },
                        new { requirementId = "DEMO-REG-05", kind = "access-control", expected = "anonymous denied", steps = "request private API" }
                    },
                    contextGaps = Array.Empty<string>()
                }),
                1 => "{\"findings\":[]}",
                2 => JsonSerializer.Serialize(new
                {
                    tests = new[]
                    {
                        Test("Positive", "DEMO-REG-01", "positive", "Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);",
                            "await client.PostAsJsonAsync(\"/api/register\", new RegistrationInput(\"new@example.com\", \"secure-password-123\"))"),
                        Test("Negative", "DEMO-REG-02", "negative", "Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);",
                            "await client.PostAsJsonAsync(\"/api/register\", new RegistrationInput(\"bad\", \"secure-password-123\"))"),
                        Test("Access", "DEMO-REG-05", "access-control", "Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);",
                            "await client.GetAsync(\"/api/me\")")
                    }
                }),
                _ => throw new InvalidOperationException("Unexpected LLM call.")
            };
            return Task.FromResult(new LlmResponse(json, 20, 30));
        }

        private static object Test(string name, string id, string kind, string assertion, string request) => new
        {
            path = $"demo/Demo.Tests/Generated/{name}.cs",
            requirementIds = new[] { id },
            kind,
            source = $$"""
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
                        HttpResponseMessage response = {{request}};
                        {{assertion}}
                    }
                }
                """
        };
    }
}
