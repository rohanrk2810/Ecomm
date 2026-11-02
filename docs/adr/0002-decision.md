# ADR-0002: Identity and browser sessions
Status: Proposed / pending approval

## Context
Baseline SCP-ARCH-001 requires this decision before the dependent feature or production deployment.

## Decision
Production provider choice remains pending. Local development uses OIDC with BFF HttpOnly cookies and CSRF protection.

User confirmed customer email/password sign-in and deferred mobile OTP in chat on
02 November 2026. This confirms the credential method, not a provider or consent policy.
Proposed implementation: dedicated ASP.NET Core Identity + OpenIddict server using
its own PostgreSQL database; gateway BFF uses authorization code + PKCE and HttpOnly
cookies. Staff MFA is required before staff access is enabled. No hand-written JWT issuer.

Development implementation now includes registration, email verification (ADR-0016),
OpenIddict authorization code with S256 PKCE and a gateway BFF backed by protected
Redis tickets. Exact first-party callbacks use implicit consent. No refresh tokens,
third-party clients or staff access are enabled. MFA-enabled accounts fail closed.
The user authorized continued implementation; production provider/consent approval
remains pending. See [session behavior and setup](../customer-sessions.md).

## Alternatives
Evaluate client-approved managed offerings and self-hosting against cost, security and operating capacity.

## Consequences
Reversible local registration implementation is in progress. This does not imply approval
of a production identity provider, consent policy or deployment.

## Security and operations impact
Owner must document credentials, isolation, failure modes, monitoring and recovery before approval.

## Approval
Owner: pending. Approver: pending. Date: pending. Evidence: pending.
