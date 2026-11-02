# Codex Project Instructions — Supermarket E-Commerce Platform

## Read first

Before writing or modifying code, read `MASTER_TECHNICAL_SPECIFICATION.md` completely enough to understand the current task and its dependencies. Follow this file and the approved ADRs. This is a new, standalone supermarket e-commerce product. Do not import assumptions, modules, entities or architecture from unrelated projects.

## Non-negotiable architecture

1. Backend: ASP.NET Core Web API on .NET 10 LTS. Frontend: Angular 22.
2. Microservices are mandatory. Every service is independently buildable/deployable and owns its database, migrations, credentials and contracts.
3. Never query or write another service's database. Integrate through versioned APIs or Kafka events.
4. Use PostgreSQL for authoritative transactional data, Kafka for business events, Redis for caching/ephemeral coordination, YARP as the public gateway, and OpenTelemetry for observability.
5. Implement the transactional outbox and consumer inbox/idempotency for event-driven business flows. Assume Kafka delivery can be repeated. Do not claim end-to-end exactly-once behavior.
6. Inventory correctness must rely on database transactions/atomic updates and concurrency controls, not a Redis lock.
7. Payment correctness must rely on durable idempotency, signature-verified provider webhooks and reconciliation. A browser redirect is not proof of payment.
8. Angular route guards are UX only. Every API must enforce authorization, object ownership and branch scope.
9. Never hardcode client-specific business values, secrets, prices, tax rates, roles, URLs, operating hours, delivery fees, promotions, slot capacity, or provider credentials. Use validated runtime/business configuration.
10. Do not store raw card data/CVV. Use provider-hosted checkout or tokenization.

## Implementation discipline

- Work in small, reviewable vertical slices. Do not scaffold every service with empty classes and claim completion.
- Before implementing a feature, identify its owning service, authoritative data, API/event contracts, authorization rules, failure modes and tests.
- If a requirement is ambiguous or depends on client decisions (tax, legal invoice rules, payment provider, stock policy, branch policy, UOM, delivery rules), record an assumption and create/update an ADR. Do not invent the value.
- Do not broaden scope or silently change service boundaries. Propose an ADR for material architecture changes.
- Keep controllers/endpoints thin. Place business invariants in domain/application layers.
- Do not expose EF entities as API DTOs or Kafka contracts.
- Use async I/O, propagate `CancellationToken`, use `decimal` for money, UTC timestamps, parameterized SQL and explicit DTO mapping.
- Keep nullable reference types enabled. Avoid `.Result`, `.Wait()`, fire-and-forget request work, static mutable state and broad catch-and-ignore blocks.
- Use typed configuration with startup validation. Store secrets in a secret manager or environment injection; do not commit `.env`, secret JSON, certificates or credentials.
- Use central package management and lockfiles. Pin versions; do not upgrade packages casually.
- Add health/readiness checks, structured logs, metrics/traces and safe correlation IDs.
- Do not log tokens, passwords, OTPs, card data, full addresses or unnecessary PII.
- Every sensitive operation (price change, stock adjustment, refund, invoice correction, role change, export) requires authorization and audit.

## Required workflow for every task

1. Inspect the existing repository and current branch before changing anything.
2. State the implementation plan briefly in the task notes/PR description.
3. Implement the smallest coherent vertical slice.
4. Add/update EF migration(s) for the owning service; never change a production schema manually.
5. Update OpenAPI and event schemas/contracts where applicable.
6. Add unit tests and the relevant integration/contract/E2E tests. Include negative authorization tests for protected operations.
7. Run formatter/linter, build, tests and relevant static/security scans. Never report a command passed unless it was actually run.
8. Inspect git diff for unrelated changes, secrets, generated files and accidental client data.
9. Update README, runbooks, ADRs and this specification if behavior or design changes.
10. Summarize changed files, commands actually run, test results, known gaps and next dependency.

## Service-specific rules

### API and data
- Version public routes under `/api/v1`.
- Return stable `ProblemDetails` errors; never leak stack traces or SQL errors.
- Apply server-side pagination with strict maximum page sizes.
- Enforce IDOR/object-level authorization for every resource ID received from a client.
- Use idempotency keys for checkout, payment and other retry-prone commands.
- Validate status transitions in the owning domain aggregate; do not accept arbitrary status values.

### Kafka
- Event names are facts in past tense.
- Every event has event ID, type/version, occurred timestamp, producer, aggregate ID/version, correlation/causation and trace context.
- Producer uses outbox; consumer uses inbox/deduplication.
- Consumers must tolerate duplicate and out-of-order delivery.
- Configure bounded retries and a monitored DLQ. Add replay guidance and never silently drop poison messages.
- Do not publish secrets or unnecessary PII.

### Redis
- Every key has a namespaced version and explicit TTL/eviction assumptions.
- Document invalidation and outage behavior.
- Redis is not the source of truth for stock, payments, invoices or orders.
- Do not use distributed locks as a substitute for database invariants.

### Angular
- Use standalone components, lazy-loaded routes, typed forms and typed API clients.
- Do not hardcode service URLs; use gateway/same-origin routing and runtime configuration.
- Do not put client secrets or privileged tokens in browser bundles.
- Show loading, empty, pending, success and error states accurately.
- Never show successful order/payment status before backend confirmation.
- Never rely on menu visibility or route guards for security.

## Definition of done

A task is done only when it compiles, relevant tests pass, migrations are present, API/event contracts are updated, authorization and failure paths are tested, observability/audit are included where applicable, no placeholder implementation or secret remains, and documentation is updated. If a required dependency or client decision prevents completion, explain the blocker and do not fabricate a working implementation.

## Production honesty

Never label a service or platform production-ready solely because it builds or runs locally. Production readiness requires agreed UAT, security verification, load testing, backup/restore evidence, operational runbooks, alerting, deployment rollback and client sign-off.

## Git workflow

- Commit each completed, verified vertical slice using the repository owner's existing Git identity.
- Do not add assistant/bot attribution, generated-by text or co-author trailers to commit metadata.
- Check the staged diff and secret scan before committing. Push only when explicitly requested.
