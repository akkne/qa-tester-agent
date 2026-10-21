namespace QaAgent.Application.Abstractions;

public sealed record SecretFinding(string Path, int Line, string Kind);

public interface ISecretScanner
{
    Task<IReadOnlyList<SecretFinding>> ScanAsync(
        GitSnapshot snapshot,
        CancellationToken cancellationToken);
}
