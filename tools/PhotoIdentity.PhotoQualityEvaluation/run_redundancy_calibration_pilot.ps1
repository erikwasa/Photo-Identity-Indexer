# WI-0194 one-entry-point local calibration pilot; never touches held-out reviews.
[CmdletBinding()]
param(
    [switch]$Review,
    [switch]$NoOpen,
    [ValidateRange(1024, 65535)][int]$Port = 5184
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Push-Location $root
try {
    $python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
    if (-not (Test-Path $python)) {
        throw "The existing PhotoIdentity evaluation Python environment is unavailable."
    }

    & $python -m unittest discover `
        -s tools/PhotoIdentity.PhotoQualityEvaluation `
        -p test_redundancy_pilot_package.py -v
    if ($LASTEXITCODE -ne 0) { throw "The calibration-pilot tests did not pass." }

    $candidate = "private/wi0194-full-enriched-candidates.json"
    $blind = "private/wi0194-calibration-pilot-blind-v1.json"
    $audit = "private/wi0194-calibration-pilot-audit-v1.json"
    $proxies = "private/wi0194-calibration-pilot-proxies-v1.json"
    $labels = "private/wi0194-calibration-pilot-labels-v1.json"
    if (-not (Test-Path $candidate)) {
        throw "The existing private checkpoint-6 enriched candidate manifest is missing."
    }

    $hasBlind = Test-Path $blind
    $hasAudit = Test-Path $audit
    if ($hasBlind -xor $hasAudit) {
        throw "The private blind and audit pair must exist together; do not overwrite either."
    }
    if (-not $hasBlind) {
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_pilot_package.py prepare `
            --input $candidate --blind-output $blind --audit-output $audit
        if ($LASTEXITCODE -ne 0) { throw "Pilot packaging failed." }
    }

    if (-not (Test-Path $proxies)) {
        & dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- redundancy pilot-proxies `
            --input $blind --output $proxies
        if ($LASTEXITCODE -ne 0) { throw "Current durable proxy mapping failed." }
    }

    Write-Host "WI-0194 local pilot preparation is complete. No held-out photos or labels were opened."
    if ($Review) {
        $args = @(
            "tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_pilot_review.py",
            "--blind", $blind, "--proxies", $proxies,
            "--labels", $labels, "--port", [string]$Port
        )
        if ($NoOpen) { $args += "--no-open" }
        & $python @args
        if ($LASTEXITCODE -ne 0) { throw "The loopback reviewer exited with an error." }
        if (Test-Path $labels) {
            & $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_pilot_package.py report `
                --blind $blind --audit $audit --labels $labels
            if ($LASTEXITCODE -ne 0) { throw "Calibration-only report failed." }
        }
    }
    elseif (Test-Path $labels) {
        & $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_pilot_package.py report `
            --blind $blind --audit $audit --labels $labels
        if ($LASTEXITCODE -ne 0) { throw "Calibration-only report failed." }
    }
    else {
        Write-Host "No reviews created. Use -Review only when you choose to start the calibration-only pilot."
    }
}
finally {
    Pop-Location
}
