namespace Commerce.Identity.Contracts;

public sealed record StaffBranchAccess(Guid BranchId, string[] Permissions);
public sealed record StaffAccessResponse(Guid UserId, StaffBranchAccess[] Branches);
