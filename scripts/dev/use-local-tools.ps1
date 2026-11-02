# Must be dot-sourced to update the caller environment.
$repoTools = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../.tools'))
$sdkPath = Join-Path $repoTools 'dotnet'
$nodePath = Join-Path $repoTools 'node-runtime/node_modules/node/bin'
if (Test-Path (Join-Path $sdkPath 'dotnet.exe')) {
    $env:DOTNET_ROOT = $sdkPath
    $env:PATH = "$sdkPath;$env:PATH"
}
if (Test-Path (Join-Path $nodePath 'node.exe')) {
    $npmCommand = Get-Command npm -CommandType ExternalScript,Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($npmCommand) {
        $npmCli = Join-Path (Split-Path $npmCommand.Source) 'node_modules/npm/bin/npm-cli.js'
        if (Test-Path $npmCli) {
            $env:COMMERCE_NPM_CLI = $npmCli
            $env:COMMERCE_NODE_EXE = Join-Path $nodePath 'node.exe'
            function global:npm { & $env:COMMERCE_NODE_EXE $env:COMMERCE_NPM_CLI @args }
        }
    }
    $env:PATH = "$nodePath;$env:PATH"
}
