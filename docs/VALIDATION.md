# Foundation validation — 02 November 2026

## Implemented
Standalone Git monorepo; pinned .NET SDK and central NuGet dependencies with lockfiles;
YARP gateway with fail-closed auth scheme (authenticates nobody), health endpoints,
safe ProblemDetails, server-generated correlation and opt-in OTLP tracing/metrics/logs.
Two independent Angular 22 shells, lazy routes, shared design system, validated startup
configuration, loading/startup failure states. Compose/collector, bootstrap, smoke script,
CI and Kubernetes review skeleton. Discovery preparation, ownership, threat model and ADRs.

## Commands actually run and final results
| Command | Result |
|---|---|
| dotnet restore Commerce.slnx --locked-mode | Passed |
| dotnet format Commerce.slnx --verify-no-changes --no-restore | Passed |
| dotnet build Commerce.slnx --no-restore -c Release | Passed; zero warnings/errors |
| dotnet test Commerce.slnx --no-restore -c Release | 6 passed |
| scripts/test/audit-backend.ps1 | No known vulnerable NuGet dependencies reported |
| scripts/test/smoke.ps1 against running gateway | Passed |
| npm ci --no-fund | Passed after stopping Windows dev servers |
| npm run format:check | Passed |
| npm run build | Both production builds passed |
| npm test | Storefront 10 + operations 1 passed |
| npm audit --audit-level=high | 0 vulnerabilities reported |
| gitleaks v8.28.0 dir . --redact --no-banner | No leaks in source; generated tool/dependency/build paths excluded |
| docker compose -f deploy/docker-compose.local.yml config --quiet | Passed with validation-only environment placeholders |
| scripts/dev/bootstrap.ps1 -DependenciesOnly (Docker follow-up) | Passed; four dependency health checks and collector HTTP health verified |
| py scripts/test/infrastructure-smoke.py | PostgreSQL transaction, Redis, Kafka, authenticated Blob storage and OTLP checks passed |
| docker compose --profile app build gateway | Passed; locked restore and Release publish inside Linux SDK container |
| docker compose --profile app up -d gateway --no-build | Passed; gateway runs as UID 1654 |
| scripts/test/smoke.ps1 against container gateway | Passed on localhost:5080; collector exported the resulting three spans |
| git diff --cached piped to Gitleaks stdin | No leaks in staged source; generated .env is ignored and untracked |

Observed storefront and operations pages in local browser; runtime app names/headings rendered.
The foundation initially had no business database. Identity registration now owns its
database and initial migration, UserRegistered contract and transactional outbox. No inbound
consumer exists yet, so there is no consumer inbox in this slice.

## Corrected during verification
Angular startup test originally called an internal API: corrected to actual TestBed initialization.
Initial npm peer resolution failed: aligned supported Vitest 5 version and generated lockfile.
Clean install initially encountered a running esbuild file lock: stopped dev servers and reran successfully.
Secret scanner initially inspected examples inside downloaded Gitleaks tooling: generated paths
are now explicitly excluded while all application source is scanned with default rules.

## Outstanding gates and limitations
Phase 0 approval is pending. Customer email/password sign-in with mobile OTP deferred was
confirmed by the user in chat. Remaining accountant/provider/business decisions stay pending.
The original Docker engine blocker is resolved. Direct registry pulls still encountered EOFs;
a scoped host-IPv4 importer verified official image digests and imported all seven dependency
and gateway base images. Local bootstrap, five functional dependency checks, gateway Linux
container build/run and real gateway-to-collector trace export have now passed.
Windows rejected PostgreSQL host port 5432; configurable local default is now 15432.
Local storage uses Azurite emulator (ADR-0015); final production storage/API remains pending.
Kubernetes is unapplied, uses explicit review placeholders and provisional resource sizing.
CI workflow is authored but has not run remotely. Full SAST/DAST/container/IaC scans, SBOM,
signing, release deployment, backups/restores, load tests, UAT and production readiness remain future work.
Gateway readiness covers gateway startup only. Identity registration readiness additionally
checks database connectivity and applied migrations. Login, verification delivery, customer,
catalog, stock, order, payment, fulfilment, billing, notifications and reporting remain pending.

## Next dependencies
Continue Phase 2 with the confirmed email/password method. The proposed dedicated ASP.NET
Core Identity + OpenIddict provider/BFF design is recorded in ADR-0002; provider and core
discovery approval remain pending. Do not mistake the credential-method answer for those approvals.
No unrelated business services were scaffolded and no remote repository was published.

## Phase 2 registration slice — completed local checks
- Foundation committed as a2f3b5b; Identity backend committed as 4808f81.
- Explicit EF migration generation/application and locked Linux Identity/gateway builds passed.
- `dotnet format Commerce.slnx --verify-no-changes --no-restore` passed.
- `dotnet test Commerce.slnx --no-restore -c Release` passed: 6 gateway + 9 Identity tests.
- PostgreSQL tests cover password hashing/unconfirmed state, concurrent case-varied duplicates,
  invalid input, forbidden role/confirmation fields, real outbox-failure transaction rollback,
  closed privileged routes and rate limiting. Fixture checks the resolved test DB before migrations.
- `npm run build` passed for both applications; `npm test` passed: 14 storefront + 1 operations.
- Registration UI tests cover policy outage, backend confirmation, duplicate clicks, password
  clearing, invalid forms and rate-limit errors. Browser preview showed the real server policy.
- `py scripts/test/identity-smoke.py` passed through Docker gateway, Identity/PostgreSQL,
  acknowledged native Kafka publish and independent Kafka consumption. No email/password in event.
- NuGet audit reported no known vulnerabilities. Staged source secret scan passed.
- Browser registration page rendered with email/password labels and actual runtime policy.

Corrected during this slice: generated migration style rules, strict JSON error mapping,
JSONB test query, deferred DI configuration/rate limits and test database resolution guards.
Registration creates an unconfirmed account only. No email, login/session, reset, OIDC,
staff MFA, branch authorization or production readiness is claimed. Local registration smoke
accounts and synthetic test records are intentionally retained. Remote CI has not run.
