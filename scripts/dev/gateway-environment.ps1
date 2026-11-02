# Gateway receives its own configuration and OIDC client credential, never Identity SQL credentials.
$gatewayRepoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot 'use-local-tools.ps1')
$gatewayValues=@{}
foreach($line in [IO.File]::ReadAllLines((Join-Path $gatewayRepoRoot '.env'))) {
    if($line -and -not $line.StartsWith('#')){$pair=$line -split '=',2;if($pair.Count -eq 2){$gatewayValues[$pair[0]]=$pair[1]}}
}
if(-not $gatewayValues['OIDC_CLIENT_SECRET']){throw 'Provision the local OIDC client first.'}
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Bff__Enabled='true'
$env:Bff__Authority='http://localhost:5082/'
$env:Bff__InternalAuthority='http://localhost:5082/'
$env:Bff__PublicAppOrigin='http://localhost:4200'
$env:Bff__ClientId='commerce-storefront'
$env:Bff__ClientSecret=$gatewayValues['OIDC_CLIENT_SECRET']
$env:Bff__RedisConnectionString='localhost:6379,abortConnect=false'
$env:Bff__KeyDirectory=Join-Path $gatewayRepoRoot '.tools/gateway-keys'
