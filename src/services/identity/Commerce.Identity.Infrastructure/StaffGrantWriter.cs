using System.Text.Json;
using Commerce.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
namespace Commerce.Identity.Infrastructure;

public enum StaffGrantOutcome { Denied, InvalidTarget, Changed, Unchanged }
public sealed class StaffGrantAudit
{
    public Guid Id { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid TargetUserId { get; set; }
    public Guid BranchId { get; set; }
    public Guid RoleId { get; set; }
    public bool Granted { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}
public sealed class StaffGrantWriter(IdentityDatabase db, StaffAccessReader access, TimeProvider clock)
{
    public const string Permission = "identity.staff.grants.write";
    public async Task<StaffGrantOutcome> ChangeAsync(Guid actor, string? sid, Guid target, Guid branch, Guid role,
        bool grant, string correlation, CancellationToken ct)
    {
        if (actor == Guid.Empty || target == Guid.Empty || branch == Guid.Empty || role == Guid.Empty) return StaffGrantOutcome.Denied;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var emails = await db.Users.Where(x => x.Id == actor || x.Id == target).Select(x => x.NormalizedEmail).ToListAsync(ct);
        foreach (var email in emails.Where(x => x is not null).Distinct().Order(StringComparer.Ordinal))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({email}, 0))", ct);
        if (!await access.AllowedAsync(actor.ToString(), sid, ct)) return StaffGrantOutcome.Denied;
        var callerPermissions = await (from assignment in db.StaffBranchRoles
                                       join permission in db.StaffRolePermissions on assignment.RoleId equals permission.RoleId
                                       where assignment.UserId == actor && assignment.BranchId == branch
                                       select permission.PermissionCode).Distinct().ToListAsync(ct);
        if (!callerPermissions.Contains(Permission)) return StaffGrantOutcome.Denied;
        var rolePermissions = await db.StaffRolePermissions.Where(x => x.RoleId == role).Select(x => x.PermissionCode).ToListAsync(ct);
        // Delegation and revocation cannot exceed the caller's own rights in this branch.
        if (rolePermissions.Count == 0 || rolePermissions.Except(callerPermissions, StringComparer.Ordinal).Any()) return StaffGrantOutcome.Denied;
        if (!await db.Users.AnyAsync(x => x.Id == target && x.EmailConfirmed && x.TwoFactorEnabled, ct)) return StaffGrantOutcome.InvalidTarget;
        var membership = await db.StaffMemberships.FindAsync([target], ct);
        var assignmentRow = await db.StaffBranchRoles.FindAsync([target, branch, role], ct);
        if ((grant && assignmentRow is not null && membership?.IsActive == true) || (!grant && assignmentRow is null)) return StaffGrantOutcome.Unchanged;
        if (grant && membership?.IsActive == false && await db.StaffBranchRoles.AnyAsync(x => x.UserId == target, ct)) return StaffGrantOutcome.Denied;
        if (membership is null) { membership = new StaffMembership { UserId = target }; db.StaffMemberships.Add(membership); }
        if (grant)
        {
            membership.IsActive = true;
            db.StaffBranchRoles.Add(new StaffBranchRole { UserId = target, BranchId = branch, RoleId = role });
        }
        else db.StaffBranchRoles.Remove(assignmentRow!);
        membership.Version++;
        await db.SaveChangesAsync(ct);
        if (!grant && !await db.StaffBranchRoles.AnyAsync(x => x.UserId == target, ct)) membership.IsActive = false;
        await db.SaveChangesAsync(ct);
        var snapshot = await access.ReadAsync(target, ct);
        var eventId = Guid.NewGuid(); var now = clock.GetUtcNow();
        db.StaffGrantAudits.Add(new StaffGrantAudit
        {
            Id = eventId,
            ActorUserId = actor,
            TargetUserId = target,
            BranchId = branch,
            RoleId = role,
            Granted = grant,
            OccurredAtUtc = now,
            CorrelationId = correlation
        });
        db.Outbox.Add(new OutboxMessage
        {
            Id = eventId,
            CreatedAtUtc = now,
            Payload = JsonSerializer.Serialize(
            new EventEnvelope<StaffAccessChanged>(eventId, "StaffAccessChanged", 1, now, "commerce.identity", target.ToString(),
                membership.Version, correlation, correlation, System.Diagnostics.Activity.Current?.Id, new StaffAccessChanged(snapshot)), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return StaffGrantOutcome.Changed;
    }
}
