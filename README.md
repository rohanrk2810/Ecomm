# Supermarket Commerce Platform
Standalone platform based on SCP-ARCH-001. Current scope: discovery preparation and
Phase 1 foundation. Commerce, login, inventory and payment are not implemented.
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
dotnet run --project src/gateway/Commerce.Gateway
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
Then `dotnet run --project src/gateway/Commerce.Gateway` and, in src/frontend,
`npm run start:storefront` or `npm run start:operations` in separate terminals.

## Checks
```powershell
dotnet restore Commerce.slnx --locked-mode
dotnet format Commerce.slnx --verify-no-changes --no-restore
dotnet build Commerce.slnx --no-restore -c Release
dotnet test Commerce.slnx --no-restore -c Release
dotnet list Commerce.slnx package --vulnerable --include-transitive
./scripts/test/smoke.ps1
py scripts/test/infrastructure-smoke.py
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
No production readiness claim. No EF migrations are needed until a service owns business data.

Local Docker dependencies have now passed transactional database, Redis, Kafka,
authenticated object storage and OTLP smoke checks. PostgreSQL uses localhost:15432
(override POSTGRES_PORT); its internal container port remains 5432. See the runbook
for the verified IPv4 image-import workaround if Docker Desktop registry downloads fail.
Infrastructure smoke checks require Python 3.11+ (`python` on Linux/macOS, `py` on Windows).
