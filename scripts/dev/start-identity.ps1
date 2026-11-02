$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'identity-environment.ps1')
Set-Location $identityRepoRoot
dotnet run --no-launch-profile --project src/services/identity/Commerce.Identity.Api --urls http://localhost:5082
exit $LASTEXITCODE
