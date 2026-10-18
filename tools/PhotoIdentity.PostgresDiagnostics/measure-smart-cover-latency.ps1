[CmdletBinding()]
param(
    [string]$BaseUrl = "http://127.0.0.1:5080",
    [ValidateRange(1, 5)][int]$Repetitions = 2,
    [ValidateRange(5, 120)][int]$TimeoutSeconds = 35
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Read-only request measurements. Identifiers, names, filters, response bodies
# and credentials are never printed or written.
$root = $BaseUrl.TrimEnd("/")
if ($root -notmatch '^https?://(127\.0\.0\.1|localhost)(:\d+)?$') {
    throw "Only a loopback Photo Identity API URL is permitted."
}

$definitions = @(Invoke-RestMethod -Uri "$root/api/smart-collections" -TimeoutSec $TimeoutSeconds)
$definitions = @($definitions | Sort-Object { [string]$_.id })

if ($definitions.Count -eq 0) {
    Write-Host "No saved Smart Collections available for evaluation."
    return
}

$results = [System.Collections.Generic.List[object]]::new()
for ($trial = 1; $trial -le $Repetitions; $trial++) {
    for ($i = 0; $i -lt $definitions.Count; $i++) {
        $id = [string]$definitions[$i].id
        if ($id -notmatch '^[0-9a-fA-F-]{36}$') {
            throw "A saved Smart Collection has an invalid identifier; aborting."
        }

        $uri = "$root/api/smart-collections/$id/query?offset=0&limit=1"
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $httpStatus = "error"

        try {
            $response = Invoke-WebRequest -Uri $uri -TimeoutSec $TimeoutSeconds -ErrorAction Stop
            $httpStatus = [string][int]$response.StatusCode
        }
        catch {
            $responseStatus = $_.Exception.Response
            if ($null -ne $responseStatus) {
                $httpStatus = [string][int]$responseStatus.StatusCode
            }
        }
        finally {
            $stopwatch.Stop()
        }

        $results.Add([pscustomobject]@{
            Trial = $trial
            CollectionOrdinal = $i + 1
            Status = $httpStatus
            ElapsedMs = [math]::Round($stopwatch.Elapsed.TotalMilliseconds)
        })
    }
}

# Ordinals are stable within this session; expensive requests are sequential.
$results | Format-Table -AutoSize
$failures = @($results | Where-Object Status -ne "200")
Write-Host ("Saved collections: {0}; trials: {1}; requests: {2}; non-200: {3}" -f $definitions.Count, $Repetitions, $results.Count, $failures.Count)
Write-Host "Five slowest requests (collection ordinals only):"
$results | Sort-Object ElapsedMs -Descending | Select-Object -First 5 | Format-Table -AutoSize
