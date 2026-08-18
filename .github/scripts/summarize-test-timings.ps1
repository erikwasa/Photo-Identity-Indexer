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
    $classByTestId = @{}
    foreach ($definition in @($document.TestRun.TestDefinitions.UnitTest)) {
        if ($null -ne $definition.TestMethod) {
            $classByTestId[[string] $definition.id] = [string] $definition.TestMethod.className
        }
    }

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

        $testId = [string] $result.testId
        $className = if ($classByTestId.ContainsKey($testId)) {
            [string] $classByTestId[$testId]
        }
        else {
            $nameWithoutArguments = $testName
            $argumentIndex = $nameWithoutArguments.IndexOf('(')
            if ($argumentIndex -ge 0) {
                $nameWithoutArguments = $nameWithoutArguments.Substring(0, $argumentIndex)
            }
            $lastDot = $nameWithoutArguments.LastIndexOf('.')
            if ($lastDot -gt 0) { $nameWithoutArguments.Substring(0, $lastDot) } else { $nameWithoutArguments }
        }

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
            $total = ($items | Measure-Object durationMilliseconds -Sum).Sum
            $maximum = ($items | Measure-Object durationMilliseconds -Maximum).Maximum
            [pscustomobject]@{
                className = $_.Name
                testCount = $items.Count
                failedCount = @($items | Where-Object { $_.outcome -eq "Failed" }).Count
                totalDurationMilliseconds = [Math]::Round([double]($total ?? 0), 3)
                maximumTestDurationMilliseconds = [Math]::Round([double]($maximum ?? 0), 3)
            }
        } |
        Sort-Object totalDurationMilliseconds -Descending |
        Select-Object -First $Top
)

$shardSummaries = @(
    $results |
        Group-Object resultFile |
        ForEach-Object {
            $items = @($_.Group)
            $total = ($items | Measure-Object durationMilliseconds -Sum).Sum
            [pscustomobject]@{
                resultFile = $_.Name
                testCount = $items.Count
                failedCount = @($items | Where-Object { $_.outcome -eq "Failed" }).Count
                totalRecordedDurationMilliseconds = [Math]::Round([double]($total ?? 0), 3)
            }
        } |
        Sort-Object resultFile
)

$totalRecordedDuration = if ($results.Count -gt 0) {
    [double](($results | Measure-Object durationMilliseconds -Sum).Sum ?? 0)
}
else {
    0.0
}

$summary = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    resultFiles = @($trxFiles | ForEach-Object { $_.Name })
    testCount = $results.Count
    failedCount = @($results | Where-Object { $_.outcome -eq "Failed" }).Count
    totalRecordedDurationMilliseconds = [Math]::Round($totalRecordedDuration, 3)
    shards = $shardSummaries
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
    $lines.Add("No TRX timing results were available. The integration test step may have been skipped or failed before producing results.")
}
else {
    $lines.Add("")
    $lines.Add("Recorded $($results.Count) tests across $($trxFiles.Count) TRX file(s); failures: $($summary.failedCount).")

    if ($shardSummaries.Count -gt 1) {
        $lines.Add("")
        $lines.Add("### Shard recorded durations")
        $lines.Add("")
        $lines.Add("| Result file | Tests | Failed | Recorded test time |")
        $lines.Add("|---|---:|---:|---:|")
        foreach ($shard in $shardSummaries) {
            $lines.Add("| $($shard.resultFile) | $($shard.testCount) | $($shard.failedCount) | {0:N2}s |" -f ($shard.totalRecordedDurationMilliseconds / 1000.0))
        }
    }

    $lines.Add("")
    $lines.Add("### Slowest classes")
    $lines.Add("")
    $lines.Add("| Class | Tests | Failed | Total | Slowest test |")
    $lines.Add("|---|---:|---:|---:|---:|")
    foreach ($item in $classSummaries) {
        $row = "| $($item.className) | $($item.testCount) | $($item.failedCount) | {0:N2}s | {1:N2}s |" -f
            ($item.totalDurationMilliseconds / 1000.0),
            ($item.maximumTestDurationMilliseconds / 1000.0)
        $lines.Add($row)
    }

    $lines.Add("")
    $lines.Add("### Slowest tests")
    $lines.Add("")
    $lines.Add("| Test | Outcome | Duration |")
    $lines.Add("|---|---|---:|")
    foreach ($item in $slowestTests) {
        $row = "| $($item.testName) | $($item.outcome) | {0:N2}s |" -f ($item.durationMilliseconds / 1000.0)
        $lines.Add($row)
    }
}

$lines | Set-Content -Path $markdownPath -Encoding utf8
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    $lines | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

Write-Host "Wrote test timing evidence to $jsonPath and $markdownPath."
