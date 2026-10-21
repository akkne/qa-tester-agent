namespace QaAgent.Application.Abstractions;

public sealed record Requirement(string Id, string Text);

public interface ISpecificationReader
{
    Task<IReadOnlyList<Requirement>> ReadAsync(
        GitSnapshot snapshot,
        ProjectProfile profile,
        CancellationToken cancellationToken);
}
