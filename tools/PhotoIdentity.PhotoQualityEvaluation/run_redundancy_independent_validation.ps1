# WI-0194 bundled independent held-out review, separate from calibration.
# By default only verifies/prepares. -Review explicitly opens HELD-OUT photos.
[CmdletBinding()]
param(
    [switch]$Review,
    [switch]$NoOpen,
    [ValidateRange(1024,65535)][int]$Port = 5185
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Push-Location $root
try {
    $python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
    if (-not (Test-Path $python)) {
        throw "Private evaluation Python environment is not configured."
    }

    & $python -m unittest discover `
        -s tools/PhotoIdentity.PhotoQualityEvaluation `
        -p test_redundancy_validation_package.py -v
    if ($LASTEXITCODE -ne 0) {
        throw "Synthetic held-out validation tests failed. No local cohort should be opened."
    }

    $source = "private/wi0194-full-enriched-candidates.json"
    $calibration = "private/wi0194-calibration-pilot-blind-v1.json"
    $blind = "private/wi0194-heldout-validation-blind-v1.json"
    $audit = "private/wi0194-heldout-validation-audit-v1.json"
    $proxies = "private/wi0194-heldout-validation-proxies-v1.json"
    $labels = "private/wi0194-heldout-validation-labels-v1.json"

    if (-not (Test-Path $source) -or -not (Test-Path $calibration)) {
        throw "Immutable private candidate data and original calibration blind are required."
    }
    if ((Test-Path $blind) -xor (Test-Path $audit)) {
        throw "Held-out validation blind and frozen audit must be created together."
    }
    if (-not (Test-Path $blind)) {
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_validation_package.py prepare `
            --input $source --calibration-blind $calibration `
            --blind-output $blind --audit-output $audit
        if ($LASTEXITCODE -ne 0) {
            throw "Held-out contamination gate failed. Do not begin any review."
        }
    }

    if (-not (Test-Path $proxies)) {
        & dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- redundancy pilot-proxies `
            --input $blind --output $proxies --validation-audit $audit
        if ($LASTEXITCODE -ne 0) {
            throw "Held-out durable proxy verification failed. No review was started."
        }
    }

    Write-Host "WI-0194 frozen held-out candidate cohort prepared. Human labels remain separate."
    if ($Review) {
        $reviewArgs = @(
            "tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_pilot_review.py",
            "--blind", $blind, "--proxies", $proxies, "--labels", $labels,
            "--port", [string]$Port, "--heldout-validation"
        )
        if ($NoOpen) { $reviewArgs += "--no-open" }
        & $python @reviewArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Held-out reviewer exited with a failure."
        }
    }
    if (Test-Path $labels) {
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_validation_package.py report `
            --blind $blind --audit $audit --labels $labels
        if ($LASTEXITCODE -ne 0) { throw "Held-out aggregate report failed." }
    }
    else {
        Write-Host "Held-out labels are absent; use -Review only after approving this bounded pilot."
    }
}
finally {
    Pop-Location
}
