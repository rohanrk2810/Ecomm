using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Commerce.Identity.Infrastructure;

public sealed class Account : IdentityUser<Guid>;
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public int Attempts { get; set; }
}
public sealed class IdentityDatabase(DbContextOptions<IdentityDatabase> options)
    : IdentityDbContext<Account, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.Entity<Account>().HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("Outbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Payload).HasColumnType("jsonb");
            entity.HasIndex(x => x.CreatedAtUtc).HasFilter("\"PublishedAtUtc\" IS NULL");
        });
    }
}
public sealed class IdentityDatabaseFactory : IDesignTimeDbContextFactory<IdentityDatabase>
{
    public IdentityDatabase CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Identity")
            ?? throw new InvalidOperationException("Inject ConnectionStrings__Identity for migration commands.");
        return new IdentityDatabase(new DbContextOptionsBuilder<IdentityDatabase>().UseNpgsql(connection).Options);
    }
}
