# Local development
Use .NET SDK from global.json and Node 22.22.3 or a compatible approved runtime.
The ignored .tools directory on the original workstation contains portable development tools.
Dot-source scripts/dev/use-local-tools.ps1 to use them; clean checkouts need normal SDK installs.

Start Docker Desktop with Linux containers. From repo root run:
`powershell -NoProfile -File scripts/dev/bootstrap.ps1`.
It generates random local credentials in ignored .env, starts dependencies and builds apps.
Do not print/copy .env into tickets, build logs or commits. Restrict local file permissions.
PostgreSQL currently has no business database. Provision separate DB/users with the first service;
the bootstrap administrator is for local provisioning only.

Gateway readiness checks its own startup only; it does not claim Kafka/database readiness
because gateway uses neither yet. Compose health checks validate local dependencies separately.
OTLP export is opt-in via OTEL_EXPORTER_OTLP_ENDPOINT. Collector debug exporter is local-only.
No application seeds exist because there are no business services or migrations yet.

Stop: `docker compose --env-file .env -f deploy/docker-compose.local.yml down`.
This preserves named volumes. Do not remove volumes unless intentionally resetting local data.
If credentials are lost, recover .env from your private local backup or deliberately reset your own
development stack; generating new credentials does not update an initialized PostgreSQL volume.

Redis data is disposable. Kafka broker is single-node plaintext for local development.
Kubernetes is a review-only skeleton with required replacements; do not apply unchanged.
Production requires approved hosting, TLS, secret manager, identity, network policies,
backups/restores, alerts, rollback, security/load tests and UAT. No production deployment exists.

## Local object storage
Azurite Blob emulator is a reversible local default (ADR-0015); production provider
remains pending. Use account commerce, key from OBJECT_STORAGE_KEY in ignored .env,
and Blob endpoint http://127.0.0.1:10000/commerce. There is no object-storage UI.
This emulator speaks Azure Blob, not S3. Its probe checks socket availability only.
Actual authenticated object operations must be tested with the storage adapter.

## Verified dependency checks
PostgreSQL host port defaults to 15432 because Windows rejected binding 5432 here.
Override POSTGRES_PORT in ignored .env if needed; containers still use postgres:5432.
Run `py scripts/test/infrastructure-smoke.py` after dependency bootstrap (Python 3.11+).
It rolls back temporary PostgreSQL data, deletes its Redis key, deletes its Kafka topic,
uploads/reads/deletes a synthetic Blob container and checks collector health/trace acceptance.
These are dependency checks, not commerce/outbox/idempotency validation.

## Docker Desktop registry EOF workaround
The engine is working but direct registry downloads can fail. On this workstation,
host IPv4 transfers work. Optional fallback (normal TLS verification stays enabled):

```powershell
py scripts/dev/pull-image-ipv4.py postgres:18.0-alpine redis:8.2.2-alpine apache/kafka:4.1.0 mcr.microsoft.com/azure-storage/azurite:3.35.0 otel/opentelemetry-collector-contrib:0.135.0
./scripts/dev/bootstrap.ps1 -DependenciesOnly
```

The helper allows only project official repositories with explicit version tags, selects
linux/amd64, verifies platform-manifest/config/compressed and uncompressed layer SHA256,
then uses docker load. It does not modify global Docker/network settings. Files are temporary
under ignored .tools/image-import and removed after each import. Allow several GB free disk.
This fallback supports gzip/uncompressed layers and linux/amd64 only. Prefer normal pulls
on systems without the registry problem. Base image patch/security updates remain reviewed work.

For the gateway's pinned Linux base images, the same fallback supports:
`py scripts/dev/pull-image-ipv4.py mcr.microsoft.com/dotnet/sdk:10.0.403 mcr.microsoft.com/dotnet/aspnet:10.0.14`.
Then build/run the gateway using:

```powershell
docker compose --env-file .env -f deploy/docker-compose.local.yml --profile app build gateway
docker compose --env-file .env -f deploy/docker-compose.local.yml --profile app up -d gateway --no-build
./scripts/test/smoke.ps1
```

Stop a previously running host gateway before binding the container to the same localhost:5080.
The container is verified as non-root UID 1654. Its OTLP exporter targets the local collector.
