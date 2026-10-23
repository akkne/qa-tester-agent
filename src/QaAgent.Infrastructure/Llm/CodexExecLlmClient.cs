using System.Text.Json;
using Microsoft.Extensions.Logging;
using QaAgent.Application.Abstractions;

namespace QaAgent.Infrastructure.Llm;

public sealed class CodexExecLlmClient(
    ICodexExecRunner runner,
    CodexExecOptions options,
    ILogger<CodexExecLlmClient> logger,
    ICodexActivityStore? activityStore = null) : ILlmClient
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

        async Task RecordAsync(string kind, string text)
        {
            if (request.RunId is not { } runId || activityStore is null) return;
            try
            {
                await activityStore.AppendAsync(runId, request.Stage, kind, text, cancellationToken);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(error, "Could not save Codex activity for run {RunId}", runId);
            }
        }

        await RecordAsync("status", "Codex started.");
        CodexExecResult result;
        try
        {
            result = await runner.RunAsync(request.Prompt, request.JsonSchema, options, cancellationToken,
                async line =>
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    JsonElement root = document.RootElement;
                    if (!root.TryGetProperty("type", out JsonElement type) ||
                        type.GetString() != "item.completed" ||
                        !root.TryGetProperty("item", out JsonElement item) ||
                        !item.TryGetProperty("type", out JsonElement itemType) ||
                        itemType.GetString() != "agent_message" ||
                        !item.TryGetProperty("text", out JsonElement message) ||
                        message.ValueKind != JsonValueKind.String) return;
                    string? text = message.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) await RecordAsync("message", text);
                });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            await RecordAsync("status", "Codex stopped: " + error.Message);
            throw;
        }
        await RecordAsync("status", $"Codex finished. Tokens: {result.InputTokens} in, {result.OutputTokens} out.");
        if (string.IsNullOrWhiteSpace(result.Json) || result.Json.Length > 256_000)
        {
            throw new InvalidDataException("Codex returned empty or oversized JSON.");
        }

        using JsonDocument response = JsonDocument.Parse(result.Json);
        if (response.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Codex response must be a JSON object.");
        }

        if (result.InputTokens < 0 || result.OutputTokens < 0)
        {
            throw new InvalidDataException("Codex returned invalid token usage.");
        }

        logger.LogInformation("Codex LLM call completed: model={Model}, inputTokens={InputTokens}, outputTokens={OutputTokens}",
            options.Model ?? "CLI default", result.InputTokens, result.OutputTokens);
        return new LlmResponse(result.Json, result.InputTokens, result.OutputTokens);
    }
}
