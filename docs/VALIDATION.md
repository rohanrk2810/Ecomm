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
checks database connectivity and applied migrations. Login, production verification delivery, customer,
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

## Phase 2 email verification — completed local checks
- Backend commit b268a27 adds maintained Identity tokens, protected mail queue, single-use
  expiring challenges, persistent resend cooldown, explicit EF migration and UserEmailVerified v1.
- 23 backend tests passed (17 Identity + 6 gateway), including expiration, replay,
  cross-account misuse, concurrent confirmation, resend cooldown and old-link invalidation.
- 20 frontend tests passed (19 storefront + 1 operations). The five new tests cover secret
  URL removal, explicit confirmation, duplicate-click protection, invalid links, safe retries
  and neutral resend state. Both frontend production builds and formatting checks passed.
- Locked Linux Identity/gateway builds and explicit migration passed. Mailpit capture verified
  actual SMTP delivery. Container restart preserved keys/challenge state; confirmation succeeded
  after restart, replay failed and an independent Kafka consumer saw the PII-free verified event.
- NuGet audit reported no known vulnerabilities. Source secret scan passed. Browser inspection
  showed the resend form. No real recipient email was sent; SMTP was captured locally.
- Git author and committer were checked as the repository owner's configured identity;
  no assistant co-author/attribution trailer is present. No remote push was performed.

OIDC/BFF login/logout, password recovery, staff MFA/branch permissions and production
sender/key approvals remain pending. SMTP retry/dead-letter state exists; operational alerting,
retention/replay tooling and production key custody are still required before release.

## Phase 2 customer sessions — completed local checks

- Release solution build: zero warnings/errors. Backend formatting verification passed.
- 34 backend tests passed: 26 Identity integration and 8 gateway tests. New coverage
  includes real code/S256 exchange, code replay, wrong/missing PKCE, unverified accounts,
  incorrect passwords, CSRF, exact redirect registration, durable session revocation,
  MFA bypass denial and disabled-BFF behavior. Invalid authorization requests use the
  provider's protocol response format; tests enforce rejection rather than assuming JSON.
- 23 frontend tests passed (22 storefront, 1 operations); both production builds and
  Prettier verification passed. Account tests cover loading/anonymous state, a native
  CSRF-protected logout form and dependency failure. Browser inspection showed the
  account screen and sign-in/registration/verification links.
- Locked Docker builds passed. Real Mailpit SMTP verification followed by public-browser
  OIDC S256 login succeeded through Angular's proxy. Identity/gateway container restart
  preserved the session through persistent certificates, protection keys and Redis.
  Missing logout CSRF was rejected, provider logout confirmation completed, old BFF
  cookie replay remained anonymous and a forged login callback returned 400.
- NuGet and npm audits reported no known vulnerabilities. Staged source secret scan
  and whitespace checks passed. Only maintained server-side OIDC packages are installed;
  no vulnerable-package warning was suppressed.

Password recovery, staff MFA/permissions, production provider/consent approval, deployment
security, key rotation, session/audit retention and operational sign-off remain pending.

## Local customer password recovery

- 36 backend tests passed (28 Identity integration, 8 gateway), including neutral unknown/
  unverified requests, cooldown, password-policy rejection without consuming the link,
  cross-account rejection, successful reset, replay rejection, old/new password checks
  and durable session revocation. Existing OIDC and verification checks still pass.
- Real Docker Identity/gateway builds, explicit migration and Mailpit SMTP recovery smoke
  passed: captured reset mail, successful password replacement and consumed-link rejection.
- Frontend tests cover immediate fragment clearing, explicit submission, duplicate submit
  prevention and clearing password controls after success. Full frontend checks are recorded
  in the task result. NuGet and npm dependency audits reported no known vulnerabilities.
- Recovery uses independent database challenges and an explicit queued-mail kind. No new
  package or secret is introduced. Production readiness is not claimed. Additional operational
  load/abuse testing, retention and production sender/key approval remain release work.

## MFA verification first slice

38 backend tests passed (30 Identity integration, 8 gateway). New cases enroll synthetic
accounts via the maintained UserManager test setup, submit the real CSRF-protected password
form with a valid authenticator/recovery code, and reject reuse from a separate browser.
The existing missing-second-factor denial test still passes. No actual staff account was
created, enrolled or granted access. Enrollment UI, MFA assurance claims and role/branch
authorization remain separate required work. See docs/mfa-login.md for the precise scope.

MFA slice: locked Docker build and explicit migration passed. Existing native SMTP/OIDC
login, restart persistence, CSRF logout and forged-callback smoke passed after deployment.
Backend formatting verification, NuGet audit and staged source secret scan passed.

## Authenticator enrollment ? local checks

- 39 backend tests passed (31 Identity integration, 8 gateway). The new enrollment test
  checks CSRF, password reauthentication, inactive MFA before confirmation, protected
  authenticator/recovery values in PostgreSQL, invalid first code, activation, one-time
  recovery display, enrollment-code replay denial and single-use recovery redemption.
- 24 frontend tests passed and both frontend production builds passed. Browser inspection
  showed the Identity setup form with labeled password/code inputs and explicit actions.
- Locked Docker builds and explicit AddMfaEnrollment migration passed. A synthetic account
  completed real SMTP verification and OIDC login, enrolled its authenticator through the
  provider form, lost its old BFF session, received recovery codes only on confirmation,
  failed password-only login and completed OIDC again using a recovery code.
- No production staff account was created or granted privileges. No real key or recovery
  code was printed by smoke tests. Production factor reset, key custody, staff permissions
  and authoritative MFA assurance remain separate work.

## MFA session assurance

40 backend tests passed (31 Identity, 9 gateway). MFA login now also verifies the ID token
and introspection amr, confirms durable MFA timestamps, removes assurance and checks that
the old access token immediately becomes inactive. The gateway policy rejects anonymous
and password-only identities. Locked Docker builds, explicit migration and the full native
SMTP/OIDC/enrollment/recovery-login smoke passed; the final BFF session reports verified MFA.
No role or staff privilege is granted by this authentication-strength policy.

## Current staff branch grants

42 backend tests passed (32 Identity, 10 gateway). The new end-to-end API integration
checks missing/forged bearer tokens, provider-cookie-only access, password-only staff,
MFA customers without membership, exact current-user branch grants despite an injected
user ID, ProblemDetails denial and live membership revocation. The gateway rejects a
caller bearer without BFF authentication. NuGet audit reported no known vulnerabilities.
Locked Docker builds, explicit migration and the SMTP/OIDC/MFA smoke passed; an enrolled
customer still received 403 and could not substitute a forged bearer on the staff route.
No staff account or role list was seeded by this feature. Production staff administration
and branch-owner validation remain separate work, documented in docs/staff-access-plan.md.

## Requested local demo account

45 backend tests passed (35 Identity, 10 gateway). Demo tests reject production/real-email
provisioning, verify atomic audit/registration events, preserve enrolled factors and revoked
grants on rerun, and reject mismatched credentials without resetting an existing account.
The explicit development CLI was run against the local Identity container using a generated
example.invalid account. Actual OIDC login passed and staff access remained 403 without MFA.
The account was deliberately left unenrolled so its owner can add an authenticator privately.
Credentials/configuration are in ignored .tools files; no password or factor was printed.
No new package or migration was introduced by demo provisioning. See docs/demo-staff.md.

## Operations access and audited role changes

- Operations access page: 28 frontend tests passed (23 storefront, 5 operations), both
  production builds passed; revoked/unavailable access clears previously displayed grants.
- Role changes: 46 backend tests passed (36 Identity, 10 gateway). Coverage includes exact
  branch authority, bounded delegation, unenrolled targets, concurrent duplicate grants,
  repeated revocation, live permission removal, audit/outbox counts and snapshot versions.
- Four actual synthetic StaffAccessChanged events passed the Draft 2020-12 JSON schema.
- Locked Docker builds, explicit migration and full SMTP/OIDC/MFA smoke passed. Unsafe
  staff writes without CSRF returned 400; a valid-MFA customer with CSRF still received 403.
- No real employee role was changed. Staff editing forms and role-definition administration
  are not represented as complete. Repository commits use the owner's requested primary
  email and local SSH signatures; GitHub signing-key registration remains a separate check.
