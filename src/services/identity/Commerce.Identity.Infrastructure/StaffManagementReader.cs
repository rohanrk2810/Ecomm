using System.ComponentModel.DataAnnotations;
using Commerce.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Commerce.Identity.Infrastructure;

public sealed class StaffManagementReader(IdentityDatabase db, StaffAccessReader access, UserManager<Account> users)
{
    public async Task<bool> AllowedAsync(string? subject, string? sid, Guid branch, CancellationToken ct)
    {
        if (branch == Guid.Empty || !Guid.TryParse(subject, out var id) || !await access.AllowedAsync(subject, sid, ct)) return false;
        return await (from assignment in db.StaffBranchRoles
                      join permission in db.StaffRolePermissions on assignment.RoleId equals permission.RoleId
                      where assignment.UserId == id && assignment.BranchId == branch && permission.PermissionCode == StaffGrantWriter.Permission
                      select assignment.UserId).AnyAsync(ct);
    }
    public async Task<StaffRoleChoice[]> RolesAsync(Guid actor, Guid branch, CancellationToken ct)
    {
        var own = await (from assignment in db.StaffBranchRoles
                         join permission in db.StaffRolePermissions on assignment.RoleId equals permission.RoleId
                         where assignment.UserId == actor && assignment.BranchId == branch
                         select permission.PermissionCode).Distinct().ToListAsync(ct);
        var rows = await (from role in db.Roles
                          join permission in db.StaffRolePermissions on role.Id equals permission.RoleId
                          orderby role.Id, permission.PermissionCode
                          select new { role.Id, role.Name, permission.PermissionCode }).Take(5001).ToListAsync(ct);
        if (rows.Count > 5000) throw new InvalidOperationException("Role catalog exceeds its supported limit.");
        return rows.GroupBy(x => x.Id).Where(group => group.All(x => own.Contains(x.PermissionCode)))
            .Select(group => new StaffRoleChoice(group.Key, group.First().Name, group.Select(x => x.PermissionCode).Distinct().ToArray()))
            .OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray();
    }
    public async Task<StaffAccountChoice?> ResolveAsync(Guid actor, Guid branch, string? email, CancellationToken ct)
    {
        email = email?.Trim();
        if (string.IsNullOrEmpty(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email)) return null;
        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.EmailConfirmed || !user.TwoFactorEnabled) return null;
        var roles = await RolesAsync(actor, branch, ct); var allowed = roles.Select(x => x.Id).ToArray();
        var assigned = await db.StaffBranchRoles.Where(x => x.UserId == user.Id && x.BranchId == branch && allowed.Contains(x.RoleId))
            .Select(x => x.RoleId).ToArrayAsync(ct);
        return new StaffAccountChoice(user.Id, assigned);
    }
}
