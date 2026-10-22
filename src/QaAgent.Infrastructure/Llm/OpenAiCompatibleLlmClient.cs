using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QaAgent.Application.Abstractions;

namespace QaAgent.Infrastructure.Llm;

public sealed class OpenAiCompatibleLlmClient(HttpClient httpClient, LlmClientOptions options) : ILlmClient
{
    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);

        using JsonDocument schema = JsonDocument.Parse(request.JsonSchema);
        if (schema.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The JSON schema must be an object.", nameof(request));
        }

        using HttpRequestMessage message = new(HttpMethod.Post, "chat/completions");
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        message.Content = JsonContent.Create(new
        {
            model = options.Model,
            messages = new[] { new { role = "user", content = request.Prompt } },
            response_format = new
            {
                type = "json_schema",
                json_schema = new { name = "qa_agent_response", strict = true, schema = schema.RootElement }
            },
            max_tokens = options.MaxOutputTokens
        });

        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"LLM request failed with HTTP {(int)response.StatusCode}.", null,
                response.StatusCode);
        }

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("choices", out JsonElement choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new InvalidDataException("LLM response has no choices.");
        }

        JsonElement choice = choices[0];
        if (choice.TryGetProperty("finish_reason", out JsonElement finishReason) &&
            finishReason.ValueKind == JsonValueKind.String && finishReason.GetString() != "stop")
        {
            throw new InvalidDataException($"LLM response did not complete: {finishReason.GetString()}.");
        }

        if (!choice.TryGetProperty("message", out JsonElement reply) ||
            !reply.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(content.GetString()))
        {
            throw new InvalidDataException("LLM response has no JSON content.");
        }

        string json = content.GetString()!;
        using JsonDocument parsedContent = JsonDocument.Parse(json);
        if (parsedContent.RootElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidDataException("LLM response JSON is null.");
        }

        if (!root.TryGetProperty("usage", out JsonElement usage) ||
            !TryGetTokenCount(usage, "prompt_tokens", out int inputTokens) ||
            !TryGetTokenCount(usage, "completion_tokens", out int outputTokens))
        {
            throw new InvalidDataException("LLM response has no valid token usage.");
        }

        return new LlmResponse(json, inputTokens, outputTokens);
    }

    private static bool TryGetTokenCount(JsonElement usage, string name, out int count)
    {
        count = 0;
        return usage.ValueKind == JsonValueKind.Object &&
            usage.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out count) && count >= 0;
    }
}
