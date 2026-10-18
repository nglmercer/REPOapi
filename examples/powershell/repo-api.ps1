# PowerShell 7+. Generic client; sends one request only when explicitly run.
param(
    [Parameter(Mandatory)][ValidateSet('GET', 'POST')][string]$Method,
    [Parameter(Mandatory)][ValidatePattern('^/[^/]')][string]$Path,
    [string]$Body = '{}',
    [string]$IdempotencyKey
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:REPO_API_TOKEN)) {
    throw 'Set REPO_API_TOKEN to AuthenticationToken from your BepInEx config.'
}
$baseUrl = if ($env:REPO_API_URL) { $env:REPO_API_URL.TrimEnd('/') } else { 'http://127.0.0.1:17453' }
$request = @{
    Uri = "$baseUrl$Path"
    Method = $Method
    Headers = @{ Authorization = "Bearer $($env:REPO_API_TOKEN.Trim())" }
    TimeoutSec = 15
    MaximumRedirection = 0
}
if ($Method -eq 'POST') {
    if (-not $IdempotencyKey) { $IdempotencyKey = [Guid]::NewGuid().ToString() }
    $request.Headers['Idempotency-Key'] = $IdempotencyKey
    $request.ContentType = 'application/json'
    $request.Body = $Body
    [Console]::Error.WriteLine("Idempotency-Key: $IdempotencyKey")
}
try {
    Invoke-RestMethod @request | ConvertTo-Json -Depth 32
} catch {
    if ($_.ErrorDetails.Message) { [Console]::Error.WriteLine($_.ErrorDetails.Message) }
    if ($Method -eq 'POST') {
        [Console]::Error.WriteLine("If the outcome is unclear, retry the SAME path and body with Idempotency-Key $IdempotencyKey.")
    }
    throw
}
