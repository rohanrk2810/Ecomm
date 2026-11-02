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
    public DbSet<MfaEnrollmentChallenge> MfaEnrollmentChallenges => Set<MfaEnrollmentChallenge>();
    public DbSet<MfaCodeUse> MfaCodeUses => Set<MfaCodeUse>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<PasswordChallenge> PasswordChallenges => Set<PasswordChallenge>();
    public DbSet<EmailChallenge> EmailChallenges => Set<EmailChallenge>();
    public DbSet<CustomerSession> CustomerSessions => Set<CustomerSession>();
    public DbSet<AuthenticationAudit> AuthenticationAudits => Set<AuthenticationAudit>();
    public DbSet<VerificationEmail> VerificationEmails => Set<VerificationEmail>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.Entity<Account>().HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Entity<CustomerSession>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
        });
        builder.Entity<PasswordChallenge>(entity => { entity.HasKey(x => x.UserId); entity.Property(x => x.TokenHash).HasMaxLength(64); entity.HasOne<Account>().WithOne().HasForeignKey<PasswordChallenge>(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<MfaCodeUse>(entity => { entity.HasKey(x => new { x.UserId, x.Digest }); entity.Property(x => x.Digest).HasMaxLength(64); entity.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<MfaEnrollmentChallenge>(entity => { entity.HasKey(x => x.UserId); entity.HasOne<Account>().WithOne().HasForeignKey<MfaEnrollmentChallenge>(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<AuthenticationAudit>().HasKey(x => x.Id);
        builder.Entity<EmailChallenge>(entity =>
        {
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength();
            entity.HasOne<Account>().WithOne().HasForeignKey<EmailChallenge>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<VerificationEmail>(entity =>
        {
            entity.Property(x => x.Kind).HasMaxLength(32).HasDefaultValue("verification");
            entity.HasKey(x => x.Id);
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.NextAttemptAtUtc).HasFilter("\"DeliveredAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL AND \"AbandonedAtUtc\" IS NULL");
        });
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
        return new IdentityDatabase(new DbContextOptionsBuilder<IdentityDatabase>().UseNpgsql(connection).UseOpenIddict().Options);
    }
}
