using System.Text.Json;

namespace QaAgent.Infrastructure.Llm;

/// <summary>Reads `codex exec --json` JSONL events and returns the usage of the last completed turn.</summary>
public static class CodexEventStream
{
    public static async Task<(int Input, int Output)?> ReadUsageAsync(
        StreamReader stream, Func<string, Task>? onEvent, CancellationToken cancellationToken)
    {
        (int Input, int Output)? completed = null;
        while (await stream.ReadLineAsync(cancellationToken) is { } line)
        {
            // Docker may interleave plain text (image pulls, entrypoint notices) with the JSONL stream.
            if (!line.StartsWith('{')) continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (onEvent is not null) await onEvent(line);
            if (root.TryGetProperty("type", out JsonElement type) && type.GetString() == "turn.completed" &&
                root.TryGetProperty("usage", out JsonElement usage) &&
                usage.TryGetProperty("input_tokens", out JsonElement input) && input.TryGetInt32(out int inputTokens) &&
                usage.TryGetProperty("output_tokens", out JsonElement output) && output.TryGetInt32(out int outputTokens) &&
                inputTokens >= 0 && outputTokens >= 0)
            {
                completed = (inputTokens, outputTokens);
            }
        }

        return completed;
    }
}
