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

# Invoke-RestMethod can output a JSON array as one array-valued pipeline object.
# Wrapping that command directly in @() can accidentally create an array of arrays;
# the wrapper has no per-collection Id. Materialize the response first, then explicitly
# enumerate its entries so a one-item, many-item and empty response behave equally.
try {
    $definitionPayload = Invoke-RestMethod -Uri "$root/api/smart-collections" -TimeoutSec $TimeoutSeconds
}
catch {
    throw "Cannot retrieve saved Smart Collections. Start Photo Identity and confirm $root/health succeeds. $($_.Exception.Message)"
}
if ($null -eq $definitionPayload) {
    throw "Saved Smart Collection endpoint returned no JSON payload."
}

$definitionItems = [System.Collections.Generic.List[object]]::new()
foreach ($definition in $definitionPayload) {
    if ($null -eq $definition -or $null -eq $definition.PSObject.Properties['Id']) {
        throw "Saved Smart Collection endpoint returned an unexpected JSON item shape."
    }
    $definitionItems.Add($definition)
}
$definitions = @($definitionItems | Sort-Object -Property Id)

if ($definitions.Count -eq 0) {
    Write-Host "No saved Smart Collections available for evaluation."
    return
}

$results = [System.Collections.Generic.List[object]]::new()
for ($trial = 1; $trial -le $Repetitions; $trial++) {
    for ($i = 0; $i -lt $definitions.Count; $i++) {
        $rawId = [string]$definitions[$i].Id
        $parsedId = [Guid]::Empty
        if (-not [Guid]::TryParse($rawId, [ref]$parsedId)) {
            throw "Saved Smart Collection ordinal $($i + 1) has an invalid GUID; aborting."
        }
        $id = $parsedId.ToString('D')

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
