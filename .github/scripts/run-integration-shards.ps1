[CmdletBinding()]
param(
    [string] $Project = "tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj",
    [string] $BaselinePath = ".github/test-timing-baseline.json",
    [string] $ResultsDirectory = ".artifacts/test-results",
    [string] $ShardOutputDirectory = ".artifacts/test-shards",
    [int] $ShardCount = 2,
    [Parameter(Mandatory = $true)]
    [int] $ShardNumber
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($ShardCount -lt 2) {
    throw "ShardCount must be at least 2."
}
if ($ShardNumber -lt 1 -or $ShardNumber -gt $ShardCount) {
    throw "ShardNumber must be between 1 and ShardCount."
}

$projectPath = (Resolve-Path $Project).Path
$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json
$baselineWeights = @{}
foreach ($property in $baseline.classWeightsSeconds.PSObject.Properties) {
    $baselineWeights[$property.Name] = [double] $property.Value
}

New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $ShardOutputDirectory -Force | Out-Null

Write-Host "Discovering integration tests for shard $ShardNumber/$ShardCount..."
$listOutput = @(
    & dotnet test $projectPath `
        --configuration Release `
        --no-build `
        --no-restore `
        --list-tests 2>&1 |
        ForEach-Object { [string] $_ }
)
if ($LASTEXITCODE -ne 0) {
    $listOutput | Write-Host
    throw "Integration test discovery failed with exit code $LASTEXITCODE."
}

$marker = "The following Tests are available:"
$markerIndex = [Array]::IndexOf($listOutput, $marker)
if ($markerIndex -lt 0) {
    $listOutput | Write-Host
    throw "Could not find the test-list marker in dotnet test output."
}

$testNames = @(
    $listOutput |
        Select-Object -Skip ($markerIndex + 1) |
        Where-Object { $_ -match '^\s+\S' } |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
if ($testNames.Count -eq 0) {
    throw "Integration test discovery returned no tests."
}

$knownClasses = @($baselineWeights.Keys | Sort-Object Length -Descending)
$classTests = @{}
foreach ($testName in $testNames) {
    $className = $null
    foreach ($knownClass in $knownClasses) {
        if ($testName.StartsWith("$knownClass.", [StringComparison]::Ordinal)) {
            $className = $knownClass
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($className)) {
        $nameWithoutArguments = $testName
        $argumentIndex = $nameWithoutArguments.IndexOf('(')
        if ($argumentIndex -ge 0) {
            $nameWithoutArguments = $nameWithoutArguments.Substring(0, $argumentIndex)
        }
        $methodSeparator = $nameWithoutArguments.LastIndexOf('.')
        if ($methodSeparator -le 0) {
            throw "Could not derive a class name for discovered test '$testName'."
        }
        $className = $nameWithoutArguments.Substring(0, $methodSeparator)
    }

    if (-not $classTests.ContainsKey($className)) {
        $classTests[$className] = [System.Collections.Generic.List[string]]::new()
    }
    $classTests[$className].Add($testName)
}

$classPlan = @(
    foreach ($entry in $classTests.GetEnumerator()) {
        $testCount = $entry.Value.Count
        $weight = if ($baselineWeights.ContainsKey($entry.Key)) {
            [double] $baselineWeights[$entry.Key]
        }
        else {
            [Math]::Max(1.0, $testCount * 0.25)
        }
        [pscustomobject]@{
            className = [string] $entry.Key
            testCount = $testCount
            estimatedSeconds = $weight
            baselineKnown = $baselineWeights.ContainsKey($entry.Key)
        }
    }
) | Sort-Object estimatedSeconds -Descending

$shards = @(
    for ($index = 0; $index -lt $ShardCount; $index++) {
        [pscustomobject]@{
            number = $index + 1
            estimatedSeconds = 0.0
            testCount = 0
            classes = [System.Collections.Generic.List[object]]::new()
        }
    }
)
foreach ($class in $classPlan) {
    $target = $shards | Sort-Object estimatedSeconds, number | Select-Object -First 1
    $target.classes.Add($class)
    $target.estimatedSeconds += $class.estimatedSeconds
    $target.testCount += $class.testCount
}

$plannedTestCount = ($shards | Measure-Object testCount -Sum).Sum
$plannedClassCount = ($shards | ForEach-Object { $_.classes.Count } | Measure-Object -Sum).Sum
if ($plannedTestCount -ne $testNames.Count -or $plannedClassCount -ne $classPlan.Count) {
    throw "Shard plan does not cover the complete discovered suite."
}

$plan = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    project = $Project
    baselineRun = $baseline.sourceWorkflowRun
    baselineTestCount = $baseline.testCount
    discoveredTestCount = $testNames.Count
    discoveredClassCount = $classPlan.Count
    shardCount = $ShardCount
    selectedShard = $ShardNumber
    shards = @(
        foreach ($shard in $shards | Sort-Object number) {
            [ordered]@{
                number = $shard.number
                estimatedSeconds = [Math]::Round($shard.estimatedSeconds, 3)
                testCount = $shard.testCount
                classCount = $shard.classes.Count
                classes = @(
                    $shard.classes |
                        Sort-Object className |
                        ForEach-Object {
                            [ordered]@{
                                className = $_.className
                                testCount = $_.testCount
                                estimatedSeconds = [Math]::Round($_.estimatedSeconds, 3)
                                baselineKnown = $_.baselineKnown
                            }
                        }
                )
            }
        }
    )
}

$planJsonPath = Join-Path $ShardOutputDirectory "shard-plan-$ShardNumber.json"
$planMarkdownPath = Join-Path $ShardOutputDirectory "shard-plan-$ShardNumber.md"
$plan | ConvertTo-Json -Depth 8 | Set-Content -Path $planJsonPath -Encoding utf8

$planLines = [System.Collections.Generic.List[string]]::new()
$planLines.Add("## Integration shard plan — shard $ShardNumber/$ShardCount")
$planLines.Add("")
$planLines.Add("Discovered $($testNames.Count) tests across $($classPlan.Count) classes. Baseline: workflow #$($baseline.sourceRunNumber) / run $($baseline.sourceWorkflowRun).")
$planLines.Add("")
$planLines.Add("| Shard | Classes | Tests | Estimated baseline time |")
$planLines.Add("|---:|---:|---:|---:|")
foreach ($shard in $plan.shards) {
    $planLines.Add("| $($shard.number) | $($shard.classCount) | $($shard.testCount) | {0:N1}s |" -f $shard.estimatedSeconds)
}
$planLines | Set-Content -Path $planMarkdownPath -Encoding utf8
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    $planLines | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

$selectedShard = $shards | Where-Object number -eq $ShardNumber | Select-Object -Single
$patterns = @(
    $selectedShard.classes |
        Sort-Object className |
        ForEach-Object { "FullyQualifiedName~$($_.className)." }
)
$filter = $patterns -join "|"
$trxName = "integration-shard-$ShardNumber.trx"
$logPath = Join-Path $ShardOutputDirectory "integration-shard-$ShardNumber.log"

Write-Host "Running shard $ShardNumber/$ShardCount: $($selectedShard.testCount) tests, estimated $([Math]::Round($selectedShard.estimatedSeconds, 1))s from baseline."
& dotnet test $projectPath `
    --configuration Release `
    --no-build `
    --no-restore `
    --filter $filter `
    --logger "trx;LogFileName=$trxName" `
    --results-directory (Resolve-Path $ResultsDirectory).Path `
    2>&1 | Tee-Object -FilePath $logPath
$testExitCode = $LASTEXITCODE

$trxPath = Join-Path $ResultsDirectory $trxName
if (-not (Test-Path $trxPath)) {
    throw "Shard $ShardNumber did not produce its TRX result file."
}

[xml] $document = Get-Content $trxPath -Raw
$unitResults = @($document.TestRun.Results.UnitTestResult)
$resultIds = @($unitResults | ForEach-Object { [string] $_.testId })
$resultCount = $unitResults.Count
$uniqueResultCount = @($resultIds | Sort-Object -Unique).Count
$coverageOk =
    $resultCount -eq $selectedShard.testCount -and
    $uniqueResultCount -eq $selectedShard.testCount

$coverage = [ordered]@{
    shardNumber = $ShardNumber
    shardCount = $ShardCount
    discoveredTestCount = $testNames.Count
    plannedShardTestCount = $selectedShard.testCount
    resultCount = $resultCount
    uniqueResultCount = $uniqueResultCount
    coverageComplete = $coverageOk
    testExitCode = $testExitCode
}
$coveragePath = Join-Path $ShardOutputDirectory "coverage-$ShardNumber.json"
$coverage | ConvertTo-Json -Depth 4 | Set-Content -Path $coveragePath -Encoding utf8

Write-Host "Shard $ShardNumber coverage: planned=$($selectedShard.testCount), results=$resultCount, unique-results=$uniqueResultCount."
if (-not $coverageOk) {
    throw "Integration shard $ShardNumber coverage check failed. Every test assigned to this shard must execute exactly once."
}
if ($testExitCode -ne 0) {
    throw "Integration shard $ShardNumber failed with exit code $testExitCode."
}

Write-Host "Integration shard $ShardNumber passed and covered every assigned test exactly once."
