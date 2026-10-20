# WI-0195 one bounded, research-only local session. NEVER scores by default.
# No source/original hydration, catalogue writes or production semantics changes.
[CmdletBinding()]
param(
    [string]$Scenarios = "private/wi0195-collection-shadow-scenarios-v1.json",
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,35}$')][string]$BatchId = "first",
    [switch]$Score,
    [switch]$AcceptResearchOnlyThirdPartyWeights,
    [switch]$Review,
    [switch]$NoOpen,
    [ValidateRange(1024,65535)][int]$Port = 5187
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Push-Location $root
try {
    $python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
    if (-not (Test-Path $python)) { throw "The local evaluation Python environment is unavailable." }
    & $python -m unittest discover `
        -s tools/PhotoIdentity.PhotoQualityEvaluation `
        -p test_collection_shadow_batch_score.py -v
    if ($LASTEXITCODE -ne 0) { throw "Bounded-scoring privacy/safety tests did not pass." }
    & $python -m unittest discover `
        -s tools/PhotoIdentity.PhotoQualityEvaluation `
        -p test_collection_shadow_simulator.py -v
    if ($LASTEXITCODE -ne 0) { throw "Existing shadow simulation tests did not pass." }

    $candidateProxies = "private/wi0195-collection-candidate-proxies-v1-$BatchId.json"
    $originalScores = "private/topiq_nr-scores-600.json"
    $newScores = "private/wi0195-collection-bounded-scores-v1-$BatchId.json"
    $blind = "private/wi0195-collection-bounded-blind-v1-$BatchId.json"
    $audit = "private/wi0195-collection-bounded-audit-v1-$BatchId.json"
    $reviewProxies = "private/wi0195-collection-bounded-review-proxies-v1-$BatchId.json"
    $reviews = "private/wi0195-collection-bounded-preferences-v1-$BatchId.json"

    if (-not (Test-Path $Scenarios) -or -not (Test-Path $originalScores)) {
        throw "Existing private scenario capture and frozen TOPIQ research scores are required."
    }
    if ($AcceptResearchOnlyThirdPartyWeights -and -not $Score) {
        throw "Weight-consent switch is only valid with explicit -Score."
    }
    if ($Score -and -not $AcceptResearchOnlyThirdPartyWeights) {
        throw "Inference requires both -Score and -AcceptResearchOnlyThirdPartyWeights."
    }

    if (-not (Test-Path $candidateProxies)) {
        & dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- collection-shadow candidate-proxies `
            --scenarios $Scenarios --output $candidateProxies
        if ($LASTEXITCODE -ne 0) {
            throw "Read-only bounded current proxy mapping failed; stop before inference."
        }
    }

    & $python tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_batch_score.py plan `
        --scenarios $Scenarios --proxies $candidateProxies --existing-scores $originalScores
    if ($LASTEXITCODE -ne 0) { throw "Bounded scoring input preflight failed." }

    if ($Score -and -not (Test-Path $newScores)) {
        Write-Warning "This invokes the local third-party TOPIQ model, which may download pretrained weights."
        Write-Warning "Checkpoint identity/licensing are NOT approved for production. Use for private research ONLY."
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_batch_score.py score `
            --scenarios $Scenarios --proxies $candidateProxies --existing-scores $originalScores `
            --output $newScores --accept-research-only-third-party-weights
        if ($LASTEXITCODE -ne 0) { throw "Bounded local inference failed. Do not retry blindly." }
    }

    if (-not (Test-Path $newScores)) {
        Write-Host "PLAN COMPLETE. No model inference was performed."
        Write-Host "An authorized bounded scoring session needs explicit -Score -AcceptResearchOnlyThirdPartyWeights."
        Write-Host "No new quality evidence or collection selections were created."
        return
    }

    if ((Test-Path $blind) -xor (Test-Path $audit)) {
        throw "Bounded blind/audit files must exist together; do not overwrite either."
    }
    if (-not (Test-Path $blind)) {
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_simulator.py prepare `
            --scenarios $Scenarios --scores $newScores `
            --blind-output $blind --audit-output $audit
        if ($LASTEXITCODE -ne 0) { throw "Bounded offline collection shadow failed." }
    }
    & $python tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_simulator.py report `
        --blind $blind --audit $audit
    if ($LASTEXITCODE -ne 0) { throw "Bounded shadow summary failed." }

    $record = Get-Content -LiteralPath $audit -Raw | ConvertFrom-Json
    $changes = @($record.comparisons | Where-Object { $_.changedSelectionCount -gt 0 }).Count
    if ($changes -eq 0) {
        Write-Host "No changed collection selections. No human review is useful for identical arms."
        return
    }
    if ($Review) {
        if (-not (Test-Path $reviewProxies)) {
            & dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- collection-shadow proxies `
                --blind $blind --output $reviewProxies
            if ($LASTEXITCODE -ne 0) { throw "Read-only reviewer proxy lookup failed." }
        }
        $args = @(
            "tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_review.py",
            "--blind", $blind, "--audit", $audit, "--proxies", $reviewProxies,
            "--reviews", $reviews, "--port", [string]$Port
        )
        if ($NoOpen) { $args += "--no-open" }
        & $python @args
        if ($LASTEXITCODE -ne 0) { throw "Blinded collection reviewer failed." }
    }
    if (Test-Path $reviews) {
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_simulator.py review-report `
            --blind $blind --audit $audit --reviews $reviews
        if ($LASTEXITCODE -ne 0) { throw "Private aggregate preferences failed validation." }
    }
    else {
        Write-Host "Changed selection candidates exist. Blinded review is optional via -Review."
    }
}
finally {
    Pop-Location
}
