[CmdletBinding()]
param(
    [string] $ResultsDirectory = ".artifacts/test-results",
    [string] $OutputDirectory = ".artifacts/test-timings",
    [int] $Top = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$results = [System.Collections.Generic.List[object]]::new()
$trxFiles = @(Get-ChildItem -Path $ResultsDirectory -Filter "*.trx" -File -Recurse -ErrorAction SilentlyContinue)

foreach ($trxFile in $trxFiles) {
    [xml] $document = Get-Content $trxFile.FullName -Raw
    $unitResults = @($document.TestRun.Results.UnitTestResult)
    foreach ($result in $unitResults) {
        $testName = [string] $result.testName
        if ([string]::IsNullOrWhiteSpace($testName)) {
            continue
        }

        $duration = [TimeSpan]::Zero
        $durationText = [string] $result.duration
        if (-not [string]::IsNullOrWhiteSpace($durationText)) {
            [TimeSpan]::TryParse($durationText, [ref] $duration) | Out-Null
        }

        $lastDot = $testName.LastIndexOf('.')
        $className = if ($lastDot -gt 0) { $testName.Substring(0, $lastDot) } else { $testName }
        $results.Add([pscustomobject]@{
            testName = $testName
            className = $className
            outcome = [string] $result.outcome
            durationMilliseconds = [Math]::Round($duration.TotalMilliseconds, 3)
            resultFile = $trxFile.Name
        })
    }
}

$slowestTests = @($results | Sort-Object durationMilliseconds -Descending | Select-Object -First $Top)
$classSummaries = @(
    $results |
        Group-Object className |
        ForEach-Object {
            $items = @($_.Group)
            [pscustomobject]@{
                className = $_.Name
                testCount = $items.Count
                failedCount = @($items | Where-Object { $_.outcome -eq "Failed" }).Count
                totalDurationMilliseconds = [Math]::Round(
                    ($items | Measure-Object durationMilliseconds -Sum).Sum,
                    3)
                maximumTestDurationMilliseconds = [Math]::Round(
                    ($items | Measure-Object durationMilliseconds -Maximum).Maximum,
                    3)
            }
        } |
        Sort-Object totalDurationMilliseconds -Descending |
        Select-Object -First $Top
)

$summary = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    resultFiles = @($trxFiles | ForEach-Object { $_.Name })
    testCount = $results.Count
    failedCount = @($results | Where-Object { $_.outcome -eq "Failed" }).Count
    totalRecordedDurationMilliseconds = [Math]::Round(
        ($results | Measure-Object durationMilliseconds -Sum).Sum,
        3)
    slowestClasses = $classSummaries
    slowestTests = $slowestTests
}

$jsonPath = Join-Path $OutputDirectory "test-timings.json"
$markdownPath = Join-Path $OutputDirectory "test-timings.md"
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding utf8

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("## Integration test timing")
if ($results.Count -eq 0) {
    $lines.Add("")
    $lines.Add("No TRX timing results were available. The integration test step may have been skipped before execution.")
}
else {
    $lines.Add("")
    $lines.Add("Recorded $($results.Count) tests across $($trxFiles.Count) TRX file(s); failures: $($summary.failedCount).")
    $lines.Add("")
    $lines.Add("### Slowest classes")
    $lines.Add("")
    $lines.Add("| Class | Tests | Failed | Total | Slowest test |")
    $lines.Add("|---|---:|---:|---:|---:|")
    foreach ($item in $classSummaries) {
        $lines.Add(
            "| $($item.className) | $($item.testCount) | $($item.failedCount) | " +
            "{0:N2}s | {1:N2}s |" -f
                ($item.totalDurationMilliseconds / 1000.0),
                ($item.maximumTestDurationMilliseconds / 1000.0))
    }

    $lines.Add("")
    $lines.Add("### Slowest tests")
    $lines.Add("")
    $lines.Add("| Test | Outcome | Duration |")
    $lines.Add("|---|---|---:|")
    foreach ($item in $slowestTests) {
        $lines.Add("| $($item.testName) | $($item.outcome) | {0:N2}s |" -f ($item.durationMilliseconds / 1000.0))
    }
}

$lines | Set-Content -Path $markdownPath -Encoding utf8
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    $lines | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

Write-Host "Wrote test timing evidence to $jsonPath and $markdownPath."
