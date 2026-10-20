# WI-0195 one local research-only shadow simulation + optional blind comparison.
# No mutation of actual Search, Smart, Creative or Slideshow selection.
[CmdletBinding()]
param(
    [string]$PreviewInput = "private/wi0195-creative-preview.json",
    [ValidateRange(1,100)][int]$ShadowTargetCount = 20,
    [switch]$Review,
    [switch]$NoOpen,
    [ValidateRange(1024,65535)][int]$Port = 5186
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Push-Location $root
try {
    $python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
    if (-not (Test-Path $python)) { throw "Local PhotoIdentity evaluation Python is unavailable." }

    & $python -m unittest discover `
        -s tools/PhotoIdentity.PhotoQualityEvaluation `
        -p test_collection_shadow_simulator.py -v
    if ($LASTEXITCODE -ne 0) { throw "Shadow safety and determinism tests failed." }

    $source = "private/wi0195-collection-shadow-scenarios-v1.json"
    $scores = "private/topiq_nr-scores-600.json"
    $blind = "private/wi0195-collection-shadow-blind-v1.json"
    $audit = "private/wi0195-collection-shadow-audit-v1.json"
    $proxies = "private/wi0195-collection-shadow-proxies-v1.json"
    $reviews = "private/wi0195-collection-shadow-preferences-v1.json"

    if (-not (Test-Path $scores)) {
        throw "Existing frozen WI-0193 research score export is absent. No re-scoring is authorized."
    }
    if (-not (Test-Path $source)) {
        if (-not (Test-Path $PreviewInput)) {
            throw "Capture at least one read-only Creative preview under private/ first."
        }
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_simulator.py import-creative-preview `
            --input $PreviewInput --target-count $ShadowTargetCount --output $source
        if ($LASTEXITCODE -ne 0) { throw "Creative preview import failed." }
    }
    if ((Test-Path $blind) -xor (Test-Path $audit)) {
        throw "The private blind/audit outputs must always exist together."
    }
    if (-not (Test-Path $blind)) {
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_simulator.py prepare `
            --scenarios $source --scores $scores --blind-output $blind --audit-output $audit
        if ($LASTEXITCODE -ne 0) { throw "Offline shadow simulation failed." }
    }
    & $python tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_simulator.py report `
        --blind $blind --audit $audit
    if ($LASTEXITCODE -ne 0) { throw "Shadow aggregate preflight failed." }

    $record = Get-Content $audit -Raw | ConvertFrom-Json
    $changes = @($record.comparisons | Where-Object { $_.changedSelectionCount -gt 0 }).Count
    if ($changes -eq 0) {
        Write-Host "No changed selection scenarios at existing score coverage. Do not claim a user preference improvement."
        Write-Host "No collection-review UI is needed; share the aggregate coverage report instead."
        return
    }
    if ($Review) {
        if (-not (Test-Path $proxies)) {
            & dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- collection-shadow proxies `
                --blind $blind --output $proxies
            if ($LASTEXITCODE -ne 0) { throw "Read-only current proxy map failed." }
        }
        $args = @(
            "tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_review.py",
            "--blind", $blind, "--audit", $audit, "--proxies", $proxies,
            "--reviews", $reviews, "--port", [string]$Port
        )
        if ($NoOpen) { $args += "--no-open" }
        & $python @args
        if ($LASTEXITCODE -ne 0) { throw "Blinded local collection reviewer failed." }
    }
    if (Test-Path $reviews) {
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_simulator.py review-report `
            --blind $blind --audit $audit --reviews $reviews
        if ($LASTEXITCODE -ne 0) { throw "Blinded aggregate preference report failed." }
    }
    else {
        Write-Host "No human collection comparisons were reviewed. Use -Review only after checking shadow coverage."
    }
}
finally {
    Pop-Location
}
