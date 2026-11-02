param([switch]$DependenciesOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $repoRoot
. (Join-Path $PSScriptRoot 'use-local-tools.ps1')
function Assert-NativeSuccess([string]$Operation) {
    if ($LASTEXITCODE -ne 0) { throw "$Operation failed with exit code $LASTEXITCODE" }
}
docker info --format '{{.ServerVersion}}'
Assert-NativeSuccess 'Docker engine check'
$envPath = Join-Path $repoRoot '.env'
if (-not (Test-Path -LiteralPath $envPath)) {
    function New-LocalSecret {
        $secretBytes = New-Object byte[] 32
        $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $generator.GetBytes($secretBytes) } finally { $generator.Dispose() }
        return [Convert]::ToBase64String($secretBytes)
    }
    $contents = @(
        "POSTGRES_PASSWORD=$(New-LocalSecret)"
        "OBJECT_STORAGE_KEY=$(New-LocalSecret)"
    ) -join "`n"
    [IO.File]::WriteAllText($envPath, $contents + "`n")
}
docker compose --env-file .env -f deploy/docker-compose.local.yml up -d --wait --wait-timeout 180
Assert-NativeSuccess 'Dependency startup'
if (-not $DependenciesOnly) {
    dotnet restore Commerce.slnx --locked-mode
    Assert-NativeSuccess 'Backend restore'
    dotnet build Commerce.slnx --no-restore
    Assert-NativeSuccess 'Backend build'
    Push-Location src/frontend
    try {
        npm ci
        Assert-NativeSuccess 'Frontend restore'
        npm run build
        Assert-NativeSuccess 'Frontend build'
    } finally { Pop-Location }
}
Write-Host 'Local foundation ready. Start gateway and frontend using README commands.'
