[CmdletBinding()]
param(
    [string] $Project = "tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj",
    [string] $QuarantinePath = ".github/flaky-integration-tests.txt",
    [string] $ResultsDirectory = ".artifacts/flaky-integration-results"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$quarantinedTests = @(
    Get-Content $QuarantinePath |
        ForEach-Object { $_.Trim() } |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            -not $_.StartsWith("#", [StringComparison]::Ordinal)
        }
)

if ($quarantinedTests.Count -eq 0) {
    Write-Host "No quarantined integration tests are configured."
    exit 0
}

if (@($quarantinedTests | Sort-Object -Unique).Count -ne $quarantinedTests.Count) {
    throw "The flaky integration quarantine contains duplicate test names."
}

New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
$filter = @(
    $quarantinedTests |
        ForEach-Object { "FullyQualifiedName=$_" }
) -join "|"

$trxName = "flaky-integration-diagnostics.trx"
$logPath = Join-Path $ResultsDirectory "flaky-integration-diagnostics.log"

Write-Host "Running $($quarantinedTests.Count) quarantined integration tests once as diagnostics. No retries are used."
& dotnet test $Project `
    --configuration Release `
    --no-build `
    --no-restore `
    --filter $filter `
    --logger "trx;LogFileName=$trxName" `
    --results-directory $ResultsDirectory `
    2>&1 | Tee-Object -FilePath $logPath
$testExitCode = $LASTEXITCODE

$trxPath = Join-Path $ResultsDirectory $trxName
if (-not (Test-Path $trxPath)) {
    throw "The flaky integration diagnostic run did not produce a TRX result file."
}

[xml] $document = Get-Content $trxPath -Raw
$unitResults = @($document.TestRun.Results.UnitTestResult)
$resultNames = @($unitResults | ForEach-Object { [string] $_.testName })
$missingResults = @(
    $quarantinedTests |
        Where-Object { $_ -notin $resultNames }
)
$unexpectedResults = @(
    $resultNames |
        Where-Object { $_ -notin $quarantinedTests }
)

$summary = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    configuredTestCount = $quarantinedTests.Count
    resultCount = $unitResults.Count
    testExitCode = $testExitCode
    missingResults = $missingResults
    unexpectedResults = $unexpectedResults
    results = @(
        $unitResults |
            ForEach-Object {
                [ordered]@{
                    testName = [string] $_.testName
                    outcome = [string] $_.outcome
                    duration = [string] $_.duration
                }
            }
    )
}

$jsonPath = Join-Path $ResultsDirectory "flaky-integration-diagnostics.json"
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $jsonPath -Encoding utf8

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value "## Flaky integration diagnostics"
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value ""
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value "These tests are temporarily non-blocking under WI-0071 and were executed once with no retry."
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value ""
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value "| Test | Outcome | Duration |"
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value "|---|---|---:|"
    foreach ($result in $unitResults) {
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value "| $([string] $result.testName) | $([string] $result.outcome) | $([string] $result.duration) |"
    }
}

if ($missingResults.Count -gt 0 -or $unexpectedResults.Count -gt 0) {
    throw "Flaky diagnostic selection mismatch. Missing: $($missingResults -join ', '); unexpected: $($unexpectedResults -join ', ')."
}

if ($testExitCode -ne 0) {
    throw "One or more quarantined integration diagnostics failed. This failure is intentionally visible but the workflow step is non-blocking while WI-0071 is open."
}

Write-Host "All quarantined integration diagnostics passed in this run. Three consecutive clean diagnostic runs are required before an entry returns to the required shards."
