using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QaAgent.Application.Abstractions;
using QaAgent.Host.Composition;
using QaAgent.Infrastructure.Llm;

namespace QaAgent.IntegrationTests;

public sealed class LlmClientTests
{
    private const string Schema = """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""";

    [Fact]
    public async Task Runs_codex_exec_with_schema_prompt_and_usage()
    {
        if (OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "qa-agent-codex-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string executable = Path.Combine(directory, "fake-codex");
        string argsPath = Path.Combine(directory, "args");
        string promptPath = Path.Combine(directory, "prompt");
        string schemaCopyPath = Path.Combine(directory, "schema");
        await File.WriteAllTextAsync(executable, $$$"""
            #!/bin/sh
            printf '%s\n' "$@" > '{{{argsPath}}}'
            while [ "$#" -gt 0 ]; do
              case "$1" in
                --output-schema) cp "$2" '{{{schemaCopyPath}}}'; shift 2 ;;
                --output-last-message) answer="$2"; shift 2 ;;
                *) shift ;;
              esac
            done
            cat > '{{{promptPath}}}'
            printf '%s' '{"ok":true}' > "$answer"
            printf '%s\n' '{"type":"turn.completed","usage":{"input_tokens":12,"output_tokens":4}}'
            """);
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            CodexExecLlmClient client = new(new CodexExecRunner(),
                new CodexExecOptions { Executable = executable, Model = "test-model" },
                NullLogger<CodexExecLlmClient>.Instance);
            LlmResponse result = await client.GenerateAsync(new LlmRequest("Check this", Schema), CancellationToken.None);

            Assert.Equal("{\"ok\":true}", result.Json);
            Assert.Equal(12, result.InputTokens);
            Assert.Equal(4, result.OutputTokens);
            Assert.Equal("Check this", await File.ReadAllTextAsync(promptPath));
            Assert.Equal(Schema, await File.ReadAllTextAsync(schemaCopyPath));
            string[] args = await File.ReadAllLinesAsync(argsPath);
            Assert.Contains("exec", args);
            Assert.Contains("--json", args);
            Assert.Contains("--output-schema", args);
            Assert.Contains("--output-last-message", args);
            Assert.Contains("read-only", args);
            Assert.Contains("--ephemeral", args);
            Assert.Contains("test-model", args);
            Assert.Equal("-", args[^1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Publishes_codex_messages_before_the_process_finishes()
    {
        if (OperatingSystem.IsWindows()) return;
        string directory = Directory.CreateTempSubdirectory("qa-agent-codex-live-test-").FullName;
        string executable = Path.Combine(directory, "fake-codex");
        string release = Path.Combine(directory, "release");
        await File.WriteAllTextAsync(executable, $$$"""
            #!/bin/sh
            while [ "$#" -gt 0 ]; do
              case "$1" in
                --output-last-message) answer="$2"; shift 2 ;;
                *) shift ;;
              esac
            done
            cat >/dev/null
            printf '%s\n' '{"type":"item.completed","item":{"id":"1","type":"agent_message","text":"Checking changed behavior"}}'
            while [ ! -f '{{{release}}}' ]; do sleep 0.1; done
            printf '%s' '{"ok":true}' > "$answer"
            printf '%s\n' '{"type":"turn.completed","usage":{"input_tokens":2,"output_tokens":3}}'
            """);
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        FileCodexActivityStore store = new(Path.Combine(directory, "activity"));
        Guid runId = Guid.NewGuid();
        CodexExecLlmClient client = new(new CodexExecRunner(),
            new CodexExecOptions { Executable = executable, TimeoutSeconds = 10 },
            NullLogger<CodexExecLlmClient>.Instance, store);
        Task<LlmResponse> pending = client.GenerateAsync(new LlmRequest("Check this", Schema)
        { RunId = runId, Stage = "Plan" }, CancellationToken.None);
        try
        {
            IReadOnlyList<CodexActivityEntry> entries = [];
            using CancellationTokenSource wait = new(TimeSpan.FromSeconds(5));
            while (!wait.IsCancellationRequested)
            {
                entries = await store.ReadAsync(runId, CancellationToken.None);
                if (entries.Any(entry => entry.Kind == "message")) break;
                await Task.Delay(50);
            }
            Assert.Contains(entries, entry => entry.Stage == "Plan" && entry.Text == "Checking changed behavior");
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            await File.WriteAllTextAsync(release, "go");
            await pending;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_cli_reports_actionable_failure()
    {
        CodexExecRunner runner = new();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync("prompt", Schema, new CodexExecOptions { Executable = "/nonexistent/codex" },
                CancellationToken.None));
        Assert.Contains("not found", error.Message);
    }

    [Fact]
    public async Task Times_out_and_stops_child_process()
    {
        if (OperatingSystem.IsWindows()) return;
        string directory = Directory.CreateTempSubdirectory("qa-agent-codex-timeout-test-").FullName;
        string executable = Path.Combine(directory, "slow-codex");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nsleep 10\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            CodexExecRunner runner = new();
            await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync("prompt", Schema,
                new CodexExecOptions { Executable = executable, TimeoutSeconds = 1 }, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_missing_usage_and_invalid_json()
    {
        CodexExecLlmClient noUsage = new(new FakeRunner(new CodexExecResult("{}", -1, 0)),
            new CodexExecOptions(), NullLogger<CodexExecLlmClient>.Instance);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            noUsage.GenerateAsync(new LlmRequest("prompt", Schema), CancellationToken.None));
        CodexExecLlmClient invalidJson = new(new FakeRunner(new CodexExecResult("oops", 1, 1)),
            new CodexExecOptions(), NullLogger<CodexExecLlmClient>.Instance);
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() =>
            invalidJson.GenerateAsync(new LlmRequest("prompt", Schema), CancellationToken.None));
    }

    [Fact]
    public async Task Event_stream_skips_malformed_lines_and_keeps_reading()
    {
        using StreamReader stream = new(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "{broken\n" + """{"type":"turn.completed","usage":{"input_tokens":7,"output_tokens":3}}""" + "\n")));

        Assert.Equal((7, 3), await CodexEventStream.ReadUsageAsync(stream, null, CancellationToken.None));
    }

    [Fact]
    public void Host_registers_codex_client_without_api_configuration()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:CodexExecutable"] = "/custom/codex",
            ["Llm:TimeoutSeconds"] = "30"
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddLlmClient(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<CodexExecLlmClient>(provider.GetRequiredService<ILlmClient>());
        Assert.Equal("/custom/codex", provider.GetRequiredService<CodexExecOptions>().Executable);
        Assert.Null(provider.GetRequiredService<CodexExecOptions>().Model);
    }

    private sealed class FakeRunner(CodexExecResult response) : ICodexExecRunner
    {
        public Task<CodexExecResult> RunAsync(string prompt, string jsonSchema, CodexExecOptions options,
            CancellationToken cancellationToken, Func<string, Task>? onEvent = null) => Task.FromResult(response);
    }
}
