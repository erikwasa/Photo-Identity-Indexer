[CmdletBinding()]
param(
    [Uri]$BaseUri = "http://127.0.0.1:5080",
    [switch]$Reset,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$base = $BaseUri.AbsoluteUri.TrimEnd('/')

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("GET", "POST")][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $response = Invoke-WebRequest -Method $Method -Uri "$base$Path" -UseBasicParsing
    if ([string]::IsNullOrWhiteSpace($response.Content)) {
        return $null
    }

    return $response.Content | ConvertFrom-Json
}

$health = Invoke-JsonRequest -Method GET -Path "/health"
if ($null -eq $health -or $health.status -ne "ok") {
    throw "Photo Identity did not report a healthy API at '$base'."
}

if ($Reset) {
    Invoke-JsonRequest -Method POST -Path "/api/archive/diagnostics/throughput/reset" | Out-Null
    Write-Host "Slideshow browser diagnostics reset. Run the representative slideshow in the browser/phone, then run this script again without -Reset."
    return
}

$diagnostics = Invoke-JsonRequest -Method GET -Path "/api/archive/diagnostics/throughput"
$positionPrefix = "slideshow-browser-image-presentation-position-"

$positions = @(
    $diagnostics.stages |
        Where-Object { $_.name.StartsWith($positionPrefix, [StringComparison]::Ordinal) } |
        ForEach-Object {
            $suffix = $_.name.Substring($positionPrefix.Length)
            [int]$position = 0
            if (-not [int]::TryParse($suffix, [ref]$position)) {
                return
            }

            [ordered]@{
                sequence = $position
                count = [long]$_.count
                averageMilliseconds = [double]$_.averageMilliseconds
                maxMilliseconds = [double]$_.maxMilliseconds
            }
        } |
        Sort-Object sequence)

$stages = @(
    $diagnostics.stages |
        Where-Object { $_.name -in @(
            "slideshow-browser-image-presentation",
            "slideshow-browser-image-resource") } |
        Select-Object name, count, totalMilliseconds, averageMilliseconds, maxMilliseconds)

$counters = @(
    $diagnostics.counters |
        Where-Object { $_.name -in @(
            "slideshow-browser-prefetch-hits",
            "slideshow-browser-prefetch-misses") } |
        Select-Object name, value)

$report = [ordered]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    catalogueProvider = [string]$health.catalogueProvider
    schemaVersion = $health.schemaVersion
    browserStages = $stages
    browserPresentationSequence = $positions
    browserPrefetchCounters = $counters
    privacyNote = "The report contains only aggregate browser timing, one-based sample sequence and prefetch hit/miss counts. It omits collection names, revision identifiers, filenames, URLs, source paths and credentials."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $directory = Join-Path ([IO.Path]::GetTempPath()) "PhotoIdentity\slideshow-performance"
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $OutputPath = Join-Path $directory ("slideshow-browser-performance-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}
else {
    $OutputPath = [IO.Path]::GetFullPath($OutputPath)
    $directory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
}

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$report | ConvertTo-Json -Depth 8
Write-Host "Slideshow browser performance report: $OutputPath"
