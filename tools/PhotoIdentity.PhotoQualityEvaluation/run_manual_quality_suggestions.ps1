# WI-0629: Safe automatic SUGGESTIONS for manual photo review.
# Existing durable proxy evidence only; no source originals, cloud hydration,
# catalogue writes, exclusions or production collection modifications.
[CmdletBinding()]
param(
    [ValidateRange(1,5000)][int]$ScanLimit = 1000,
    [ValidateRange(1,1500)][int]$SuggestionLimit = 500,
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,35}$')][string]$BatchId = "pilot-01",
    [switch]$Review,
    [switch]$NoOpen,
    [ValidateRange(1024,65535)][int]$Port = 5188
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Push-Location $repo
try {
    $python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
    if (-not (Test-Path $python)) { throw "The existing PhotoIdentity Python research environment is required." }
    & $python -m unittest discover `
        -s tools/PhotoIdentity.PhotoQualityEvaluation `
        -p test_manual_quality_suggestions.py -v
    if ($LASTEXITCODE -ne 0) { throw "Private manual-suggestion safety tests failed." }

    $evidence = "private/wi0629-technical-evidence-$BatchId.json"
    $manifest = "private/wi0629-suggestions-$BatchId.json"
    $decisions = "private/wi0629-human-review-$BatchId.json"
    if (-not (Test-Path $env:PhotoIdentity__ReviewProxyRoot)) {
        throw "Existing durable review-proxy root is not configured; no original will be opened."
    }
    if (-not [bool]$env:PhotoIdentity__Postgres__ConnectionString) {
        throw "Read-only catalogue connection is not configured."
    }
    if ($env:PhotoIdentity__ReviewProxyProfileId -ne "jpeg-1600-q78") {
        throw "Set the measured review-proxy profile to jpeg-1600-q78."
    }

    if (-not (Test-Path $evidence)) {
        & dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- `
            --output $evidence --limit $ScanLimit
        if ($LASTEXITCODE -ne 0) { throw "Read-only technical evidence preparation failed." }
    }

    if (-not (Test-Path $manifest)) {
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/manual_quality_suggestions.py prepare `
            --source $evidence --output $manifest --limit $SuggestionLimit
        if ($LASTEXITCODE -ne 0) { throw "Private suggestion generation failed." }
    }
    Write-Host "WI-0629 candidate suggestions are prepared. No photos have been excluded."
    if ($Review) {
        $reviewArgs = @(
            "tools/PhotoIdentity.PhotoQualityEvaluation/manual_quality_suggestion_review.py",
            "--manifest", $manifest,
            "--state", $decisions,
            "--proxy-root", $env:PhotoIdentity__ReviewProxyRoot,
            "--port", [string]$Port
        )
        if ($NoOpen) { $reviewArgs += "--no-open" }
        & $python @reviewArgs
        if ($LASTEXITCODE -ne 0) { throw "Manual suggestion review failed." }
    }
    if (Test-Path $decisions) {
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/manual_quality_suggestions.py report `
            --manifest $manifest --state $decisions
        if ($LASTEXITCODE -ne 0) { throw "Private review report failed." }
    }
    else {
        Write-Host "No human decisions recorded yet. Use -Review only when you want to review suggestions."
    }
}
finally {
    Pop-Location
}
