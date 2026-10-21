namespace QaAgent.Application.Abstractions;

public sealed record StoredArtifact(Guid Id, string Kind, string RelativePath);

public interface IArtifactStore
{
    Task<StoredArtifact> SaveAsync(
        Guid runId,
        string kind,
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}
