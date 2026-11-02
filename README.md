# Supermarket Commerce Platform
Standalone platform based on SCP-ARCH-001. Current scope: discovery preparation and
Phase 1 foundation plus Phase 2 registration and local email verification. Shopping, login,
password reset, inventory and payment are not implemented.
Client discovery/ADR sign-off remains pending. Original documentation is preserved in
the sibling supermarket-commerce-docs folder; normative spec and AGENTS.md are copied here.

## Prerequisites
- .NET SDK 10.0.403 (global.json), Node 22.22.3, npm 10.9.2, Git.
- Docker Desktop with Linux engine running for local dependencies.
- On this workstation: `. ./scripts/dev/use-local-tools.ps1` selects ignored portable tools.

## Windows PowerShell
From repository root:
```powershell
. ./scripts/dev/use-local-tools.ps1
./scripts/dev/bootstrap.ps1
./scripts/dev/start-gateway.ps1
```
In a second terminal:
```powershell
. ./scripts/dev/use-local-tools.ps1
cd src/frontend
npm run start:storefront
```
Storefront: http://localhost:4200. Operations shell: `npm run start:operations` on port 4201.
Both use same-origin /api via local proxy to gateway on localhost:5080.
Operations shell has no privileged features and does not simulate authentication.

## Linux/macOS
Install pinned tools and PowerShell 7. Run `pwsh -File scripts/dev/bootstrap.ps1`.
Then `./scripts/dev/start-gateway.ps1` and, in src/frontend,
`npm run start:storefront` or `npm run start:operations` in separate terminals.

## Checks
Identity tests need the isolated local test database. Before the commands below, run
`./scripts/dev/provision-identity.ps1` and dot-source `./scripts/dev/identity-environment.ps1`.
The storefront registration preview is at http://localhost:4200/register. Start Identity
with `./scripts/dev/start-identity.ps1` in another terminal, following its migration setup.
Local verification emails appear at http://localhost:8025. The resend page is
http://localhost:4200/resend-verification; verification links require an explicit confirmation.

```powershell
dotnet restore Commerce.slnx --locked-mode
dotnet format Commerce.slnx --verify-no-changes --no-restore
dotnet build Commerce.slnx --no-restore -c Release
dotnet test Commerce.slnx --no-restore -c Release
dotnet list Commerce.slnx package --vulnerable --include-transitive
./scripts/test/smoke.ps1
py scripts/test/infrastructure-smoke.py
py scripts/test/identity-smoke.py
py scripts/test/verification-smoke.py
cd src/frontend
npm ci
npm run format:check
npm run build
npm test
npm audit --audit-level=high
```

Read docs/IMPLEMENTATION_PLAN.md, docs/client-discovery/DECISIONS.md,
docs/architecture.md, docs/adr and docs/runbooks/local-development.md.
Gateway OpenAPI is available only in Development at /openapi/v1.json.
Package versions and lockfiles are pinned. Image tags are pinned; digest promotion,
SBOM/signing, registry publishing and complete production security gates remain future work.
No production readiness claim. Identity now owns its database and initial EF migration.
Registration creates an unconfirmed account and a transactional UserRegistered event; it
does not issue a session. Local email verification is captured in Mailpit; see
[Identity setup](docs/identity-registration.md) and [Verification setup](docs/email-verification.md).

Local Docker dependencies have now passed transactional database, Redis, Kafka,
authenticated object storage and OTLP smoke checks. PostgreSQL uses localhost:15432
(override POSTGRES_PORT); its internal container port remains 5432. See the runbook
for the verified IPv4 image-import workaround if Docker Desktop registry downloads fail.
Infrastructure smoke checks require Python 3.11+ (`python` on Linux/macOS, `py` on Windows).

Customer OIDC sign-in/logout and the My account page are now available locally.
Follow [session setup](docs/customer-sessions.md) for explicit certificate preparation,
migration and BFF client registration before starting Identity/gateway.

Customer password recovery is available at http://localhost:4200/forgot-password.
Apply the Identity migration and see docs/customer-sessions.md for lifecycle details.

Already-enrolled accounts can use authenticator/recovery codes on the Identity login
form. Apply the latest Identity migration; see docs/mfa-login.md for scope and gaps.
