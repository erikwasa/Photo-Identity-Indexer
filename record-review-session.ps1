<#
.SYNOPSIS
Records one privacy-safe WI-0033 manual review session.

.DESCRIPTION
Calculates review throughput and interaction-effort metrics for one Windows or Pixel
session and writes JSON below .artifacts/review-verification/manual. No person names,
face identifiers, file paths, photos, embeddings or catalogue contents are recorded.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("Windows", "Pixel")]
    [string] $Device,

    [Parameter(Mandatory)]
    [ValidateRange(50, 100)]
    [int] $FacesReviewed,

    [Parameter(Mandatory)]
    [ValidateRange(0.01, 1440)]
    [double] $ActiveMinutes,

    [Parameter(Mandatory)]
    [ValidateRange(0, 10000)]
    [int] $AcceptedSuggestions,

    [Parameter(Mandatory)]
    [ValidateRange(0, 10000)]
    [int] $ExplicitActions,

    [Parameter(Mandatory)]
    [ValidateRange(0, 10000)]
    [int] $GalleryReturns,

    [Parameter(Mandatory)]
    [ValidateRange(0, 10000)]
    [int] $ImmediateUndos,

    [string[]] $FailedChecks = @(),

    [ValidateLength(0, 1000)]
    [string] $Notes = "",

    [string] $OutputDirectory = ".artifacts/review-verification/manual"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$normalizedFailedChecks = @($FailedChecks |
    ForEach-Object { $_.Trim().ToLowerInvariant() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Sort-Object -Unique)

$facesPerMinute = [Math]::Round($FacesReviewed / $ActiveMinutes, 3)
$actionsPerAcceptedSuggestion = if ($AcceptedSuggestions -gt 0) {
    [Math]::Round($ExplicitActions / $AcceptedSuggestions, 3)
}
else {
    $null
}

$report = [ordered]@{
    schemaVersion = 1
    workItem = "WI-0033"
    device = $Device
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    result = if ($normalizedFailedChecks.Count -eq 0) { "passed" } else { "failed" }
    facesReviewed = $FacesReviewed
    activeMinutes = [Math]::Round($ActiveMinutes, 3)
    facesPerMinute = $facesPerMinute
    acceptedSuggestions = $AcceptedSuggestions
    explicitActions = $ExplicitActions
    actionsPerAcceptedSuggestion = $actionsPerAcceptedSuggestion
    galleryReturns = $GalleryReturns
    immediateUndos = $ImmediateUndos
    failedChecks = $normalizedFailedChecks
    notes = $Notes.Trim()
    privacy = [ordered]@{
        containsPersonNames = $false
        containsFaceIdentifiers = $false
        containsLocalPaths = $false
        containsBiometricData = $false
    }
}

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputDirectory))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$deviceFileName = "$($Device.ToLowerInvariant())-session.json"
$devicePath = Join-Path $root $deviceFileName
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $devicePath -Encoding UTF8

$deviceReports = [System.Collections.Generic.List[object]]::new()
foreach ($name in @("windows-session.json", "pixel-session.json")) {
    $path = Join-Path $root $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $deviceReports.Add((Get-Content -LiteralPath $path -Raw | ConvertFrom-Json))
    }
}

if ($deviceReports.Count -eq 2) {
    $summary = [ordered]@{
        schemaVersion = 1
        workItem = "WI-0033"
        generatedAtUtc = [DateTime]::UtcNow.ToString("O")
        result = if (@($deviceReports | Where-Object { $_.result -ne "passed" }).Count -eq 0) {
            "passed"
        }
        else {
            "failed"
        }
        devices = $deviceReports.ToArray()
        aggregate = [ordered]@{
            facesReviewed = ($deviceReports | Measure-Object -Property facesReviewed -Sum).Sum
            activeMinutes = [Math]::Round(($deviceReports | Measure-Object -Property activeMinutes -Sum).Sum, 3)
            acceptedSuggestions = ($deviceReports | Measure-Object -Property acceptedSuggestions -Sum).Sum
            explicitActions = ($deviceReports | Measure-Object -Property explicitActions -Sum).Sum
            galleryReturns = ($deviceReports | Measure-Object -Property galleryReturns -Sum).Sum
            immediateUndos = ($deviceReports | Measure-Object -Property immediateUndos -Sum).Sum
        }
    }
    $summaryPath = Join-Path $root "manual-verification-summary.json"
    $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-Host "Updated two-device summary: $summaryPath"
}

Write-Host "Recorded $Device review session: $devicePath"
Write-Host "Faces per minute: $facesPerMinute"
if ($null -ne $actionsPerAcceptedSuggestion) {
    Write-Host "Explicit actions per accepted suggestion: $actionsPerAcceptedSuggestion"
}
else {
    Write-Host "Explicit actions per accepted suggestion: not applicable (no accepted suggestions)"
}
Write-Host "Result: $($report.result)"
