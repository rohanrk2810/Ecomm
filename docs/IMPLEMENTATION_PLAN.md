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
