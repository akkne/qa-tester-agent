using Microsoft.EntityFrameworkCore;

namespace QaAgent.Infrastructure.Persistence;

public sealed class RunDbContext(DbContextOptions<RunDbContext> options) : DbContext(options)
{
    public DbSet<RunRecord> Runs => Set<RunRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RunRecord>(entity =>
        {
            entity.ToTable("runs");
            entity.HasKey(run => run.Id);
            entity.Property(run => run.RepositoryId).HasMaxLength(200);
            entity.Property(run => run.BaseSha).HasMaxLength(40);
            entity.Property(run => run.HeadSha).HasMaxLength(40);
            entity.Property(run => run.State).HasMaxLength(32);
            entity.Property(run => run.FinalStatus).HasMaxLength(32);
            entity.HasIndex(run => new
            {
                run.RepositoryId,
                run.BaseSha,
                run.HeadSha,
                run.ProfileVersion
            }).IsUnique();
            entity.HasIndex(run => new { run.State, run.CreatedAt });
        });
    }
}
