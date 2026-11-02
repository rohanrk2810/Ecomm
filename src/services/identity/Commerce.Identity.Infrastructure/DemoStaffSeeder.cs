using System.Text.Json;
using Commerce.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Commerce.Identity.Infrastructure;

public sealed class DemoStaffSettings
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public Guid BranchId { get; set; }
    public string[] Permissions { get; set; } = [];
}
public sealed class DemoStaffSeeder(IdentityDatabase db, UserManager<Account> users, TimeProvider clock)
{
    public async Task<Guid> SeedAsync(DemoStaffSettings settings, bool development, CancellationToken ct)
    {
        if (!development || db.Database.GetDbConnection().Database is not ("commerce_identity" or "commerce_identity_tests") ||
            !settings.Email.EndsWith("@example.invalid", StringComparison.OrdinalIgnoreCase) || settings.Email.Length > 254 ||
            settings.Password.Length is < 12 or > 128 || string.IsNullOrWhiteSpace(settings.RoleName) || settings.RoleName.Length > 128 || settings.RoleId == Guid.Empty || settings.BranchId == Guid.Empty ||
            settings.Permissions.Length is < 1 or > 20 || settings.Permissions.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 128 || x.Contains('*')))
            throw new InvalidOperationException("Demo provisioning requires explicit development-only fixture configuration.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var email = users.NormalizeEmail(settings.Email)!;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({email}, 0))", ct);
        var existing = await users.FindByEmailAsync(settings.Email);
        if (existing is not null)
        {
            if (!await db.AuthenticationAudits.AnyAsync(x => x.UserId == existing.Id && x.Action == "DemoStaffProvisioned", ct) ||
                !await users.CheckPasswordAsync(existing, settings.Password))
                throw new InvalidOperationException("Existing demo credentials do not match. No account was changed.");
            // Never restore revoked grants, reset a password or replace an enrolled factor.
            await transaction.CommitAsync(ct); return existing.Id;
        }
        var user = new Account { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N"), Email = settings.Email, EmailConfirmed = true };
        if (!(await users.CreateAsync(user, settings.Password)).Succeeded)
            throw new InvalidOperationException("Demo credentials do not meet the configured password policy.");
        if (await db.Roles.AnyAsync(x => x.Id == settings.RoleId || x.NormalizedName == users.NormalizeName(settings.RoleName), ct))
            throw new InvalidOperationException("Demo role identity is already in use. No existing role was changed.");
        db.Roles.Add(new IdentityRole<Guid>
        {
            Id = settings.RoleId,
            Name = settings.RoleName,
            NormalizedName = users.NormalizeName(settings.RoleName),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        });
        foreach (var permission in settings.Permissions.Distinct(StringComparer.Ordinal))
        {
            if (!await db.StaffPermissions.AnyAsync(x => x.Code == permission, ct)) db.StaffPermissions.Add(new StaffPermission { Code = permission });
            db.StaffRolePermissions.Add(new StaffRolePermission { RoleId = settings.RoleId, PermissionCode = permission });
        }
        db.StaffMemberships.Add(new StaffMembership { UserId = user.Id, IsActive = true });
        db.StaffBranchRoles.Add(new StaffBranchRole { UserId = user.Id, RoleId = settings.RoleId, BranchId = settings.BranchId });
        var now = clock.GetUtcNow(); var correlation = Guid.NewGuid().ToString("N");
        db.AuthenticationAudits.Add(new AuthenticationAudit { Id = Guid.NewGuid(), UserId = user.Id, Action = "DemoStaffProvisioned", OccurredAtUtc = now, CorrelationId = correlation });
        var registeredId = Guid.NewGuid(); var verifiedId = Guid.NewGuid();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        db.Outbox.AddRange(new OutboxMessage
        {
            Id = registeredId,
            CreatedAtUtc = now,
            Payload = JsonSerializer.Serialize(
            new EventEnvelope<UserRegistered>(registeredId, "UserRegistered", 1, now, "commerce.identity", user.Id.ToString(), 1, correlation, correlation, null, new UserRegistered(user.Id)), json)
        },
            new OutboxMessage
            {
                Id = verifiedId,
                CreatedAtUtc = now,
                Payload = JsonSerializer.Serialize(
            new EventEnvelope<UserEmailVerified>(verifiedId, "UserEmailVerified", 1, now, "commerce.identity", user.Id.ToString(), 2, correlation, correlation, null, new UserEmailVerified(user.Id)), json)
            });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return user.Id;
    }
}
