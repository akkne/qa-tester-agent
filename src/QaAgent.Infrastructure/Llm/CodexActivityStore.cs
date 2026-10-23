using System.Text;
using System.Text.Json;

namespace QaAgent.Infrastructure.Llm;

public sealed record CodexActivityEntry(DateTimeOffset At, string Stage, string Kind, string Text);

public interface ICodexActivityStore
{
    Task AppendAsync(Guid runId, string stage, string kind, string text, CancellationToken cancellationToken);
    Task<IReadOnlyList<CodexActivityEntry>> ReadAsync(Guid runId, CancellationToken cancellationToken);
}

public sealed class FileCodexActivityStore(string root) : ICodexActivityStore
{
    private const long MaxFileBytes = 4_000_000;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string root = Path.GetFullPath(root);

    public async Task AppendAsync(Guid runId, string stage, string kind, string text, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string path = PathFor(runId);
            await using FileStream file = new(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite,
                4096, FileOptions.Asynchronous);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (file.Length >= MaxFileBytes) return;
            string line = JsonSerializer.Serialize(new CodexActivityEntry(
                DateTimeOffset.UtcNow, stage, kind, text.Length > 256_000 ? text[..256_000] + "\n…" : text)) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            await file.WriteAsync(bytes, cancellationToken);
            await file.FlushAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CodexActivityEntry>> ReadAsync(Guid runId, CancellationToken cancellationToken)
    {
        string path = PathFor(runId);
        if (!File.Exists(path)) return [];
        List<CodexActivityEntry> entries = [];
        await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            4096, FileOptions.Asynchronous);
        using StreamReader reader = new(file, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            try
            {
                CodexActivityEntry? entry = JsonSerializer.Deserialize<CodexActivityEntry>(line);
                if (entry is not null) entries.Add(entry);
            }
            catch (JsonException)
            {
                // The writer may still be appending the final line.
            }
        }
        return entries;
    }

    private string PathFor(Guid runId) => Path.Combine(root, runId.ToString("N") + ".jsonl");
}
