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

## Alternatives
Evaluate client-approved managed offerings and self-hosting against cost, security and operating capacity.

## Consequences
Only reversible foundation work may proceed. Do not assume this proposal is approved.

## Security and operations impact
Owner must document credentials, isolation, failure modes, monitoring and recovery before approval.

## Approval
Owner: pending. Approver: pending. Date: pending. Evidence: pending.
