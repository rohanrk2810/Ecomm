param([string]$GatewayUrl = 'http://localhost:5080')
$ErrorActionPreference = 'Stop'
foreach ($path in @('/health/live', '/health/ready')) {
    $response = Invoke-WebRequest "$GatewayUrl$path" -UseBasicParsing
    if ($response.StatusCode -ne 200) { throw "Health failed: $path" }
    if (-not $response.Headers['X-Correlation-ID']) { throw 'Missing correlation ID' }
}
$status = Invoke-RestMethod "$GatewayUrl/api/v1/platform/status"
if ($status.stage -ne 'foundation' -or $status.commerceAvailable -ne $false) {
    throw 'Unexpected foundation status'
}
Write-Host 'Gateway smoke checks passed.'
