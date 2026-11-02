# Staff role-management UI

Implementation plan: expose a bounded role catalog and exact-email account resolution
through Identity, reuse the existing audited grant/revoke commands, and add a lazy page
in the operations app. Verify unauthorized scopes, higher-role filtering, CSRF, review,
duplicate submission and clearing stale target/grant data before committing.

Open /staff-management in the operations app after signing in with MFA. The page loads
the caller's current branch grants and allows selecting only branches with the technical
identity.staff.grants.write capability. Branches currently display authorization references
because no authoritative branch directory/name API exists yet; operators select the
reference, never type it. Role names and permissions come from the server catalog, not a
hardcoded role list. Account lookup uses a verified, MFA-enrolled employee email rather
than asking the operator to enter a user GUID. Missing/unready accounts share one error.

GET /api/v1/identity/staff/branches/{branch}/roles returns only nonempty roles whose entire
permission set is within the caller's current branch rights. Catalog queries fail closed
above 5,000 joined permission rows. POST on the same branch's /resolve-account accepts
{email}, requires the same authority, uses the existing auth quota, and returns only a
ready target's ID and delegatable role assignments in that branch. No public directory or
email-in-query search is added. Neither endpoint writes assignments or grants privileges.

All unsafe calls fetch a same-session BFF antiforgery token and send X-CSRF-TOKEN. OAuth
tokens remain on the gateway. The UI shows the chosen account, branch and role for explicit
review; editing selections invalidates that review. Buttons prevent duplicate submission.
Only a successful backend response reports a change. Refresh/lookup failures clear usable
target state, and revoked access clears the role catalog. The backend rechecks every
mutation even if the displayed catalog becomes stale. Existing transaction/audit/outbox
behavior is reused without a new schema or library dependency.

This screen assigns existing roles to existing enrolled accounts. Invitations, creating
or editing role definitions, production branch names/lifecycle and client business-role
policy remain separate work. No real employee grants are changed by development tests.

Validation: dotnet format and the solution tests passed (36 Identity, 10 gateway).
Frontend tests passed (23 storefront, 9 operations), both production builds passed,
and Prettier validation passed. NuGet and npm audits reported no known vulnerabilities.
The staged gitleaks scan found no secrets. Browser verification confirmed the anonymous
page requires sign-in/MFA; authorized review, CSRF, grant/removal and failure behavior
are exercised with synthetic HTTP fixtures rather than real employee assignments.
Docker Identity/gateway builds and gateway readiness smoke passed. The extended MFA
smoke passed SMTP verification, enrollment/session invalidation, recovery-code OIDC,
credential-header replacement, CSRF enforcement, and denial of catalog/account lookup
and role writes for an MFA customer without staff authority. Docker startup required
the socket recovery documented in the local-development runbook.
