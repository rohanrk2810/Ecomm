# Audited branch role changes

Plan: implement a minimal grant/revoke API with live MFA, branch authorization, bounded
delegation, database concurrency, atomic audit/outbox and BFF CSRF before adding management
forms. This does not define client-specific role names or seed production staff grants.

PUT or DELETE /api/v1/identity/staff/{target}/branches/{branch}/roles/{role} requires the
CurrentStaff bearer policy. Inside the transaction the actor's live session/membership
is checked again and the actor must have identity.staff.grants.write in that exact branch.
The requested role must exist with nonempty permissions, all contained in the actor's own
current branch permissions. This restriction applies to grants and revocations. Target
accounts must be confirmed and MFA-enrolled. Global inactive memberships with retained
assignments cannot be silently reactivated by a branch grant. Removing the final role
makes membership inactive. Roles and branches are authorization references; no role name
implies global access, and business services still validate their own resource/branch data.

Repeated requests are no-ops with 204 and no duplicate audit/event. Actor and target email
advisory locks are acquired in stable order, shared with credential/MFA workflows. Each
actual change increments the target membership version and commits assignment, audit and
StaffAccessChanged v1 outbox together. Its full current grant snapshot lets future inbox
consumers apply the latest aggregate version despite duplicate/out-of-order delivery.
No email/password/token is in the event. Existing acknowledged outbox publishing is reused.

The gateway requires VerifiedMfa, strips caller credentials, supplies the server-held token,
and validates X-CSRF-TOKEN for unsafe staff requests. Obtain it from /bff/antiforgery using
the same authenticated browser session. Direct service callers use validated bearer tokens;
provider cookies cannot authorize this API. The operations assignment UI and its scoped
catalog/account lookup are described in [staff-management-ui.md](staff-management-ui.md).
A global role-definition API is not implemented. Production approval, invitations and branch lifecycle/
permission definitions remain separate work. Apply AddAuditedStaffRoleChanges explicitly.
