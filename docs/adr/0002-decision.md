# ADR-0002: Identity and browser sessions
Status: Proposed / pending approval

## Context
Baseline SCP-ARCH-001 requires this decision before the dependent feature or production deployment.

## Decision
Pending provider choice. Prefer OIDC with BFF HttpOnly cookies and CSRF protection. No login or JWT issuance implemented.

User confirmed customer email/password sign-in and deferred mobile OTP in chat on
02 November 2026. This confirms the credential method, not a provider or consent policy.
Proposed implementation: dedicated ASP.NET Core Identity + OpenIddict server using
its own PostgreSQL database; gateway BFF uses authorization code + PKCE and HttpOnly
cookies. Staff MFA is required before staff access is enabled. No hand-written JWT issuer.

Development progress: under the user's instruction to continue, the first slice now uses
ASP.NET Core Identity for unconfirmed account registration and password hashing. It issues
no token/session. OpenIddict authorization-code/PKCE, BFF, email confirmation/reset, staff
MFA and production provider approval remain subsequent work.

## Alternatives
Evaluate client-approved managed offerings and self-hosting against cost, security and operating capacity.

## Consequences
Reversible local registration implementation is in progress. This does not imply approval
of a production identity provider, consent policy or deployment.

## Security and operations impact
Owner must document credentials, isolation, failure modes, monitoring and recovery before approval.

## Approval
Owner: pending. Approver: pending. Date: pending. Evidence: pending.
