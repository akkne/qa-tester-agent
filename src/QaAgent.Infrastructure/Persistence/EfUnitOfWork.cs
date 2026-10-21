using QaAgent.Application.Abstractions;

namespace QaAgent.Infrastructure.Persistence;

public sealed class EfUnitOfWork(RunDbContext dbContext, IRunRepository runs) : IUnitOfWork
{
    public IRunRepository Runs => runs;

    public Task CommitAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
