using Commerce.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
namespace Commerce.Identity.Infrastructure;

public sealed class StaffMembership
{
    public Guid UserId { get; set; }
    public bool IsActive { get; set; }
}
public sealed class StaffPermission
{
    public string Code { get; set; } = string.Empty;
}
public sealed class StaffRolePermission
{
    public Guid RoleId { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
}
public sealed class StaffBranchRole
{
    public Guid UserId { get; set; }
    public Guid BranchId { get; set; }
    public Guid RoleId { get; set; }
}
public sealed class StaffAccessReader(IdentityDatabase db, CustomerSessionValidator sessions)
{
    public async Task<bool> AllowedAsync(string? subject, string? sid, CancellationToken ct)
    {
        if (!Guid.TryParse(subject, out var id)) return false;
        return await sessions.ActiveAsync(subject, sid, ct) && await sessions.MfaAsync(subject, sid, ct) &&
            await db.StaffMemberships.AnyAsync(x => x.UserId == id && x.IsActive, ct);
    }
    public async Task<StaffAccessResponse> ReadAsync(Guid userId, CancellationToken ct)
    {
        // Branches are opaque references, never a cross-service database join.
        var rows = await (from assignment in db.StaffBranchRoles
                          join membership in db.StaffMemberships on assignment.UserId equals membership.UserId
                          join permission in db.StaffRolePermissions on assignment.RoleId equals permission.RoleId
                          where assignment.UserId == userId && membership.IsActive
                          orderby assignment.BranchId, permission.PermissionCode
                          select new { assignment.BranchId, permission.PermissionCode }).Take(1001).ToListAsync(ct);
        if (rows.Count > 1000) throw new InvalidOperationException("Staff grant result exceeds its supported limit.");
        return new StaffAccessResponse(userId, rows.GroupBy(x => x.BranchId).Select(group =>
            new StaffBranchAccess(group.Key, group.Select(x => x.PermissionCode).Distinct().ToArray())).ToArray());
    }
}
