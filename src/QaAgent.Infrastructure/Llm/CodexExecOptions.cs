namespace QaAgent.Infrastructure.Llm;

public sealed class CodexExecOptions
{
    public string Executable { get; init; } = "codex";
    public string? Model { get; init; }
    public int TimeoutSeconds { get; init; } = 600;
}
