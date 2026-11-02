# Dot-source to set Identity-only local configuration without printing credentials.
$identityRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot 'use-local-tools.ps1')
$identityValues = @{}
foreach ($line in [IO.File]::ReadAllLines((Join-Path $identityRepoRoot '.env'))) {
    if ($line -and -not $line.StartsWith('#')) {
        $parts = $line -split '=', 2
        if ($parts.Count -eq 2) { $identityValues[$parts[0]] = $parts[1] }
    }
}
$identitySecret = $identityValues['IDENTITY_DATABASE_PASSWORD']
if (-not $identitySecret -or $identitySecret -notmatch '^[A-Za-z0-9+/]{43}=$') {
    throw 'Run scripts/dev/provision-identity.ps1 to create the local Identity credential.'
}
$identityPort = if ($identityValues['POSTGRES_PORT']) { $identityValues['POSTGRES_PORT'] } else { '15432' }
if ($identityPort -notmatch '^\d{1,5}$') { throw 'Invalid local PostgreSQL port.' }
$env:ConnectionStrings__Identity = "Host=localhost;Port=$identityPort;Database=commerce_identity;Username=commerce_identity;Password=$identitySecret;Include Error Detail=false;GSS Encryption Mode=Disable"
$env:IdentityTestConnection = "Host=localhost;Port=$identityPort;Database=commerce_identity_tests;Username=commerce_identity;Password=$identitySecret;Include Error Detail=false;GSS Encryption Mode=Disable"
$env:Messaging__BootstrapServers = 'localhost:29092'
$env:Messaging__RegistrationTopic = 'commerce.identity.users.v1'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:4317'
$env:Verification__KeyDirectory = Join-Path $identityRepoRoot '.tools/identity-keys'
$env:VerificationDelivery__Enabled = 'true'
$env:Oidc__Issuer = 'http://localhost:5082/'
$env:Oidc__PublicAppOrigin = 'http://localhost:4200'
$env:Oidc__ClientId = 'commerce-storefront'
$env:Oidc__ClientSecret = $identityValues['OIDC_CLIENT_SECRET']
$env:Oidc__CertificatePassword = $identityValues['OIDC_CERTIFICATE_PASSWORD']
$env:Oidc__SigningCertificatePath = Join-Path $identityRepoRoot '.tools/identity-keys/oidc-signing.pfx'
$env:Oidc__EncryptionCertificatePath = Join-Path $identityRepoRoot '.tools/identity-keys/oidc-encryption.pfx'
