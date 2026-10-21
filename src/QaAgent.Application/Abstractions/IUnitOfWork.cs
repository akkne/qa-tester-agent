namespace QaAgent.Application.Abstractions;

public interface IUnitOfWork
{
    IRunRepository Runs { get; }

    Task CommitAsync(CancellationToken cancellationToken);
}
