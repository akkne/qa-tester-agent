namespace QaAgent.Application.Abstractions;

public sealed record LlmRequest(string Prompt, string JsonSchema);

public sealed record LlmResponse(string Json, int InputTokens, int OutputTokens);

public interface ILlmClient
{
    Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken);
}
