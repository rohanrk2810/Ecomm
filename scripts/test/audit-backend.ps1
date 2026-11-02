$ErrorActionPreference = 'Stop'
$report = dotnet list Commerce.slnx package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) { throw 'NuGet vulnerability lookup failed.' }
$audit = ($report -join "`n") | ConvertFrom-Json
if ($audit.problems -and $audit.problems.Count -gt 0) { throw 'NuGet audit reported a feed problem.' }
foreach ($project in $audit.projects) {
    foreach ($framework in $project.frameworks) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($package.vulnerabilities.Count -gt 0) {
                throw "Vulnerable dependency: $($package.id). Review NuGet audit output."
            }
        }
    }
}
Write-Host 'No known vulnerable NuGet dependencies reported.'
