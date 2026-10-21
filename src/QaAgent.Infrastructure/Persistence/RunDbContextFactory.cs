using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QaAgent.Infrastructure.Persistence;

public sealed class RunDbContextFactory : IDesignTimeDbContextFactory<RunDbContext>
{
    public RunDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<RunDbContext> builder = new();
        builder.UseNpgsql("Host=localhost;Database=qa_agent");
        return new RunDbContext(builder.Options);
    }
}
