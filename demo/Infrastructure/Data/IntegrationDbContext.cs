// Infrastructure/Data/IntegrationDbContext.cs

using EllucianIntegrationEngine.Core;
using Microsoft.EntityFrameworkCore;

namespace EllucianIntegrationEngine.Infrastructure.Data;

public sealed class IntegrationDbContext : DbContext
{
    public IntegrationDbContext(DbContextOptions<IntegrationDbContext> options) : base(options) { }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Status, x.NextRetryAt });
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
        });
    }
}
