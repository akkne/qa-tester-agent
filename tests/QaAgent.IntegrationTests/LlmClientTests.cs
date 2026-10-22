using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QaAgent.Application.Abstractions;
using QaAgent.Host.Composition;
using QaAgent.Infrastructure.Llm;

namespace QaAgent.IntegrationTests;

public sealed class LlmClientTests
{
    [Fact]
    public async Task Sends_schema_and_returns_json_with_usage()
    {
        string? requestBody = null;
        string? authorization = null;
        using HttpClient http = new(new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            authorization = request.Headers.Authorization?.ToString();
            Assert.Equal("https://llm.example/v1/chat/completions", request.RequestUri!.ToString());
            return JsonResponse("""
                {"choices":[{"finish_reason":"stop","message":{"content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":12,"completion_tokens":4}}
                """);
        }))
        { BaseAddress = new Uri("https://llm.example/v1/") };

        OpenAiCompatibleLlmClient client = new(http,
            new LlmClientOptions { Model = "test-model", ApiKey = "test-key" });
        LlmResponse result = await client.GenerateAsync(
            new LlmRequest("Check this", """{"type":"object","properties":{"ok":{"type":"boolean"}}}"""),
            CancellationToken.None);

        Assert.Equal("{\"ok\":true}", result.Json);
        Assert.Equal(12, result.InputTokens);
        Assert.Equal(4, result.OutputTokens);
        Assert.Equal("Bearer test-key", authorization);
        using JsonDocument payload = JsonDocument.Parse(requestBody!);
        Assert.Equal("test-model", payload.RootElement.GetProperty("model").GetString());
        Assert.Equal("Check this", payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("json_schema", payload.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.True(payload.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Equal("object", payload.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("""{"choices":[{"finish_reason":"length","message":{"content":"{}"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""")]
    [InlineData("""{"choices":[{"finish_reason":"stop","message":{"content":"not-json"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""")]
    [InlineData("""{"choices":[{"finish_reason":"stop","message":{"content":"{}"}}],"usage":{"prompt_tokens":-1,"completion_tokens":1}}""")]
    public async Task Rejects_incomplete_or_invalid_responses(string body)
    {
        using HttpClient http = new(new StubHandler(_ => Task.FromResult(JsonResponse(body))))
        { BaseAddress = new Uri("https://llm.example/v1/") };
        OpenAiCompatibleLlmClient client = new(http, new LlmClientOptions { Model = "test-model" });

        await Assert.ThrowsAnyAsync<Exception>(() => client.GenerateAsync(
            new LlmRequest("Check", "{}"), CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Retries_transient_llm_failures_then_returns_result(HttpStatusCode status)
    {
        int calls = 0;
        using HttpClient http = new(new StubHandler(_ =>
        {
            calls++;
            return Task.FromResult(calls < 3
                ? new HttpResponseMessage(status)
                : JsonResponse("""
                    {"choices":[{"finish_reason":"stop","message":{"content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":2,"completion_tokens":1}}
                    """));
        }))
        { BaseAddress = new Uri("https://llm.example/v1/") };

        OpenAiCompatibleLlmClient client = new(http, new LlmClientOptions { Model = "test-model" });
        LlmResponse result = await client.GenerateAsync(new LlmRequest("Check", "{}"), CancellationToken.None);

        Assert.Equal(3, calls);
        Assert.Equal("{\"ok\":true}", result.Json);
    }

    [Fact]
    public async Task Does_not_retry_nontransient_llm_failure()
    {
        int calls = 0;
        using HttpClient http = new(new StubHandler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        }))
        { BaseAddress = new Uri("https://llm.example/v1/") };

        OpenAiCompatibleLlmClient client = new(http, new LlmClientOptions { Model = "test-model" });
        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GenerateAsync(new LlmRequest("Check", "{}"), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Stops_retrying_after_three_transient_failures()
    {
        int calls = 0;
        using HttpClient http = new(new StubHandler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        }))
        { BaseAddress = new Uri("https://llm.example/v1/") };

        OpenAiCompatibleLlmClient client = new(http, new LlmClientOptions { Model = "test-model" });
        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GenerateAsync(new LlmRequest("Check", "{}"), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Does_not_hold_worker_for_long_retry_after()
    {
        int calls = 0;
        using HttpClient http = new(new StubHandler(_ =>
        {
            calls++;
            HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return Task.FromResult(response);
        }))
        { BaseAddress = new Uri("https://llm.example/v1/") };

        OpenAiCompatibleLlmClient client = new(http, new LlmClientOptions { Model = "test-model" });
        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GenerateAsync(new LlmRequest("Check", "{}"), CancellationToken.None));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Host_registers_llm_client_from_configuration()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:BaseUrl"] = "http://localhost:9000/v1",
            ["Llm:Model"] = "local-model"
        }).Build();
        ServiceCollection services = new();
        services.AddLlmClient(configuration);
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        Assert.IsType<OpenAiCompatibleLlmClient>(provider.GetRequiredService<ILlmClient>());
        Assert.Equal("local-model", provider.GetRequiredService<LlmClientOptions>().Model);
    }

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
