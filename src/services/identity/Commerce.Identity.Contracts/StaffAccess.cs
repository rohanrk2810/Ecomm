namespace Commerce.Identity.Contracts;

public sealed record StaffBranchAccess(Guid BranchId, string[] Permissions);
public sealed record StaffAccessResponse(Guid UserId, StaffBranchAccess[] Branches);
public sealed record StaffAccessChanged(StaffAccessResponse Access);
public sealed record StaffRoleChoice(Guid Id, string? Name, string[] Permissions);
public sealed record StaffAccountLookup(string? Email);
public sealed record StaffAccountChoice(Guid UserId, Guid[] AssignedRoleIds);
