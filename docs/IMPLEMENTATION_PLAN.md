# Foundation implementation plan
Scope: Phase 0 discovery preparation and reversible Phase 1 foundation only.
No identity, customer, catalog, checkout or payment service is represented as implemented.

1. Inspect repository and installed tools; preserve original documentation.
2. Create standalone monorepo with pinned SDK/package versions and lockfiles.
3. Build a YARP gateway with safe errors, deny-by-default routing, health and telemetry.
4. Build independent Angular storefront/operations shells with validated runtime configuration.
5. Add local dependency Compose, bootstrap, smoke checks, CI and deployment skeleton.
6. Record pending discovery, security model, ownership and ADRs.
7. Run formatting, builds, tests, dependency audits and inspect the diff.

Foundation has no transactional data, database schema or business events. EF migrations,
outbox/inbox and business contracts start with the first owning business service.
Phase 0 sign-off remains pending; Phase 1 work does not resolve business decisions.

## Docker follow-up — 02 November 2026
Verify newly started engine, import pinned images through a scoped IPv4 fallback if normal
pulls fail, run dependency operations using disposable synthetic records, test gateway container
and telemetry, and update validation evidence. Record user-selected customer email/password
sign-in with mobile OTP deferred. Identity provider implementation remains a separate slice.

## Phase 2 slice 1 — customer registration
User requested continued implementation and a Git commit per completed task.
Commit foundation, then Identity registration with isolated PostgreSQL role/migrations,
runtime password policy, generic duplicate responses, quota, transactional PII-free outbox
and acknowledged Kafka publishing. Verify rollback, concurrency, extra-field rejection and
safe failure states. Separately commit the lazy Angular registration form with typed client,
server policy hints and pending/error states. Login and activation remain subsequent slices.

## Phase 2 slice 2 — email verification
Use maintained Identity tokens, persistent protected keys, expiring single-use challenges,
database resend cooldown and protected transactional mail queue. Capture SMTP locally with
Mailpit and publish a PII-free verification event. Add explicit-action verification/resend
screens. Verify expiry, replay, cross-account use, concurrency, SMTP and restart persistence.
Commit backend and frontend as independently reviewed slices using the repository owner's identity.

## Phase 2 slice 3 ? customer sessions
Implement maintained OpenIddict code/S256 PKCE in Identity and an opaque Redis-backed
BFF cookie in gateway. Add durable revocation/audit, exact callbacks and CSRF-protected
logout. Add the lazy account screen, native browser redirects, negative protocol tests
and a real SMTP/OIDC/restart smoke. Commit verified backend and UI separately.
