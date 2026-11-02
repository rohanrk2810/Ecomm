$ErrorActionPreference = 'Stop'
$demoRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $demoRepoRoot
$demoDirectory = Join-Path $demoRepoRoot '.tools'
[IO.Directory]::CreateDirectory($demoDirectory) | Out-Null
foreach ($relative in @('.tools/demo-staff-config.json', '.tools/demo-staff-account.txt')) {
    git check-ignore --quiet -- $relative
    if ($LASTEXITCODE -ne 0) { throw 'Demo credential paths must be ignored by Git before provisioning.' }
}
$demoConfigPath = Join-Path $demoDirectory 'demo-staff-config.json'
if (-not (Test-Path -LiteralPath $demoConfigPath)) {
    $randomBytes = New-Object byte[] 24
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($randomBytes) } finally { $generator.Dispose() }
    $config = [ordered]@{
        Email = ('demo-admin-' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '@example.invalid')
        Password = ('Demo!aA7' + [Convert]::ToBase64String($randomBytes))
        RoleName = 'Demo administrator'
        RoleId = [Guid]::NewGuid().ToString()
        BranchId = [Guid]::NewGuid().ToString()
        BranchName = 'Demo branch'
        Permissions = @('identity.staff.access.read', 'identity.staff.grants.write')
    }
    [IO.File]::WriteAllText($demoConfigPath, ($config | ConvertTo-Json))
}
$config = Get-Content -Raw -LiteralPath $demoConfigPath | ConvertFrom-Json
$demoValues = @{}
foreach ($property in @('Email', 'Password', 'RoleName', 'RoleId', 'BranchId')) {
    $demoValues["DemoStaff__$property"] = [string]$config.$property
}
for ($index = 0; $index -lt $config.Permissions.Count; $index++) {
    $demoValues["DemoStaff__Permissions__$index"] = [string]$config.Permissions[$index]
}
$previous = @{}
try {
    $arguments = @('compose', '--env-file', '.env', '-f', 'deploy/docker-compose.local.yml', '--profile', 'identity', 'run', '--rm', '--no-deps')
    foreach ($key in $demoValues.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $demoValues[$key], 'Process')
        # Passing the variable name keeps its secret value out of process command lines.
        $arguments += @('-e', $key)
    }
    & docker @arguments identity --seed-demo-staff
    if ($LASTEXITCODE -ne 0) { throw 'Demo provisioning failed; credentials and existing grants were not reset.' }
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
$credentialPath = Join-Path $demoDirectory 'demo-staff-account.txt'
$instructions = @(
    'LOCAL DEVELOPMENT DEMO ONLY'
    "Email: $($config.Email)"
    "Password: $($config.Password)"
    "Role label: $($config.RoleName)"
    "Demo authorization branch: $($config.BranchName) ($($config.BranchId))"
    'Sign in: http://localhost:4200/account'
    'Choose Set up an authenticator, enter the current password, add the key manually and confirm the app code.'
    'Save recovery codes privately. Sign in again with password and a fresh app/recovery code.'
    'Current staff grants: http://localhost:4200/api/v1/identity/staff-access'
    'This fixture has no production or commerce privileges; staff administration UI is still being built.'
    'Rerunning this script never restores revoked staff grants or replaces passwords/MFA.'
) -join "`r`n"
[IO.File]::WriteAllText($credentialPath, $instructions + "`r`n")
Write-Host "Demo credentials saved privately to $credentialPath (ignored by Git)."
