# Staff access: next vertical slice

Implement an authenticated, versioned read of the caller's own current staff grants.
Identity owns staff membership, a configurable permission registry, role-permission links
and explicit branch-role assignments. No role names or business permission list is seeded.
Customer registration never creates staff membership. No public grant-writing API exists.

The gateway requires verified MFA and forwards only its server-held access token to the
Identity route. Identity independently validates that bearer token with maintained local
OIDC validation, checks the live session/MFA proof and active staff membership, then reads
only that subject's assignments. No browser userId/role/branch claim grants authority.

Test anonymous/forged/password-only/customer access, current own-branch grants, injected
user IDs, and immediate membership revocation. Every service will still enforce its own
business-resource ownership/branch policy; this read endpoint does not grant commerce
operations. Staff invitation, audited grant administration, approved role definitions,
branch existence validation by the owning service and operations UI remain later work.

## Implemented read contract

GET /api/v1/identity/staff-access returns {userId, branches:[{branchId,permissions:[]}]}
for the current verified staff subject only. It accepts no caller-selected user ID.
Assignments are explicit per branch; no wildcard/global permission is inferred from a
role name or a missing branch. Permission definitions and role names are persisted data,
not a hardcoded Admin/Manager/Staff list. New memberships default inactive; no grants are
seeded by startup, migrations, registration or MFA enrollment.

Identity validates token signatures, encryption, issuer, audience, expiry, persisted token
and authorization status through OpenIddict local validation. It then checks current
session/MFA proof and membership on each request. Role/permission reads are uncached and
limited to 1,000 joined grant rows; oversize results fail closed rather than truncate.
Branch IDs are references; assignment creation must validate them through the owning
branch service when that write workflow is implemented. This endpoint reads grants only;
it neither creates staff nor authorizes inventory, refunds or other service operations.

YARP's staff route uses VerifiedMfa and an explicit IdentityAccessToken transform. The
transform removes incoming Authorization/Cookie headers and supplies the server ticket's
access token. Identity rejects a provider cookie on its own, even on the same host. No
bearer token is returned to Angular. Missing credentials return 401; a verified customer,
password-only staff session or inactive membership returns 403. Grant revocation is checked
live instead of trusting long-lived role claims.

Apply AddStaffBranchGrants explicitly. Two maintained OpenIddict validation integration
packages at the existing 7.7.2 version are added and pinned. No client/unused data-protection
validation stack is enabled. Subsequent audited grant/revoke APIs and the operations
assignment UI are documented in [staff-role-changes.md](staff-role-changes.md) and
[staff-management-ui.md](staff-management-ui.md). Invitations, branch lifecycle and
client-specific role definitions remain separate work.
