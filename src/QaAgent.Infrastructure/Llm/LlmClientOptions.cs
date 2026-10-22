namespace QaAgent.Infrastructure.Llm;

public sealed class LlmClientOptions
{
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";
    public string Model { get; init; } = "";
    public string? ApiKey { get; init; }
    public int TimeoutSeconds { get; init; } = 120;
    public int MaxOutputTokens { get; init; } = 4096;
}
