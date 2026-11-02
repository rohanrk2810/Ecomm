$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'gateway-environment.ps1')
Set-Location $gatewayRepoRoot
dotnet run --no-launch-profile --project src/gateway/Commerce.Gateway --urls http://localhost:5080
exit $LASTEXITCODE
