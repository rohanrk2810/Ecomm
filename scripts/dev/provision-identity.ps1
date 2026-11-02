$ErrorActionPreference = 'Stop'
$identityRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $identityRepoRoot
$localEnv = Join-Path $identityRepoRoot '.env'
if (-not (Test-Path -LiteralPath $localEnv)) { throw 'Run dependency bootstrap first.' }
if (-not ([IO.File]::ReadAllText($localEnv) -match '(?m)^IDENTITY_DATABASE_PASSWORD=')) {
    $randomBytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($randomBytes) } finally { $generator.Dispose() }
    [IO.File]::AppendAllText($localEnv, "IDENTITY_DATABASE_PASSWORD=$([Convert]::ToBase64String($randomBytes))`n")
}
. (Join-Path $PSScriptRoot 'identity-environment.ps1')
# psql quote-literal formatting prevents SQL injection; no credentials are written to SQL files/logs.
$provisionSql = @'
\getenv identity_password IDENTITY_BOOTSTRAP_SECRET
SELECT format('CREATE ROLE commerce_identity LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', :'identity_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'commerce_identity') \gexec
SELECT 'CREATE DATABASE commerce_identity OWNER commerce_identity'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'commerce_identity') \gexec
SELECT 'CREATE DATABASE commerce_identity_tests OWNER commerce_identity'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'commerce_identity_tests') \gexec
REVOKE ALL ON DATABASE commerce_identity FROM PUBLIC;
REVOKE ALL ON DATABASE commerce_identity_tests FROM PUBLIC;
'@
$provisionSql | docker compose --env-file .env -f deploy/docker-compose.local.yml exec -T -e "IDENTITY_BOOTSTRAP_SECRET=$identitySecret" postgres psql -X -q -v ON_ERROR_STOP=1 -U commerce_bootstrap -d postgres
if ($LASTEXITCODE -ne 0) { throw 'Identity database provisioning failed.' }
docker compose --env-file .env -f deploy/docker-compose.local.yml exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --create --if-not-exists --topic commerce.identity.users.v1 --partitions 1 --replication-factor 1
if ($LASTEXITCODE -ne 0) { throw 'Identity topic provisioning failed.' }
Write-Host 'Identity-only local databases, role and Kafka topic are provisioned.'
