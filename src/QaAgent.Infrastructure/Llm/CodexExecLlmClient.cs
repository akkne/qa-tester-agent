using System.Text.Json;
using Microsoft.Extensions.Logging;
using QaAgent.Application.Abstractions;

namespace QaAgent.Infrastructure.Llm;

public sealed class CodexExecLlmClient(
    ICodexExecRunner runner,
    CodexExecOptions options,
    ILogger<CodexExecLlmClient> logger) : ILlmClient
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

        CodexExecResult result = await runner.RunAsync(request.Prompt, request.JsonSchema, options, cancellationToken);
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
