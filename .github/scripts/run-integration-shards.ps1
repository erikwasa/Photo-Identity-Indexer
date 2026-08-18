[CmdletBinding()]
param(
    [string] $Project = "tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj",
    [string] $BaselinePath = ".github/test-timing-baseline.json",
    [string] $ResultsDirectory = ".artifacts/test-results",
    [string] $ShardOutputDirectory = ".artifacts/test-shards",
    [int] $ShardCount = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($ShardCount -lt 2) {
    throw "ShardCount must be at least 2."
}

$projectPath = (Resolve-Path $Project).Path
$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json
$baselineWeights = @{}
foreach ($property in $baseline.classWeightsSeconds.PSObject.Properties) {
    $baselineWeights[$property.Name] = [double] $property.Value
}

New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $ShardOutputDirectory -Force | Out-Null

Write-Host "Discovering integration tests..."
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
            # New classes stay covered automatically. Give them enough estimated weight
            # to avoid all unknown coverage falling into one shard before the next baseline refresh.
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

$plan = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    project = $Project
    baselineRun = $baseline.sourceWorkflowRun
    baselineTestCount = $baseline.testCount
    discoveredTestCount = $testNames.Count
    discoveredClassCount = $classPlan.Count
    shardCount = $ShardCount
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

$planJsonPath = Join-Path $ShardOutputDirectory "shard-plan.json"
$planMarkdownPath = Join-Path $ShardOutputDirectory "shard-plan.md"
$plan | ConvertTo-Json -Depth 8 | Set-Content -Path $planJsonPath -Encoding utf8

$planLines = [System.Collections.Generic.List[string]]::new()
$planLines.Add("## Integration shard plan")
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

$running = [System.Collections.Generic.List[object]]::new()

foreach ($shard in $shards | Sort-Object number) {
    $patterns = @(
        $shard.classes |
            Sort-Object className |
            ForEach-Object { "FullyQualifiedName~$($_.className)." }
    )
    $filter = $patterns -join "|"
    $trxName = "integration-shard-$($shard.number).trx"

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "dotnet"
    $startInfo.WorkingDirectory = $PWD.Path
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in @(
        "test",
        $projectPath,
        "--configuration", "Release",
        "--no-build",
        "--no-restore",
        "--filter", $filter,
        "--logger", "trx;LogFileName=$trxName",
        "--results-directory", (Resolve-Path $ResultsDirectory).Path
    )) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Failed to start integration shard $($shard.number)."
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    $running.Add([pscustomobject]@{
        number = $shard.number
        process = $process
        stdoutTask = $stdoutTask
        stderrTask = $stderrTask
        logPath = Join-Path $ShardOutputDirectory "integration-shard-$($shard.number).log"
        estimatedSeconds = $shard.estimatedSeconds
        testCount = $shard.testCount
    })

    Write-Host "Started integration shard $($shard.number): $($shard.testCount) tests, estimated $([Math]::Round($shard.estimatedSeconds, 1))s."
}

$failedShards = [System.Collections.Generic.List[int]]::new()

foreach ($run in $running | Sort-Object number) {
    $run.process.WaitForExit()
    $stdout = $run.stdoutTask.GetAwaiter().GetResult()
    $stderr = $run.stderrTask.GetAwaiter().GetResult()

    @(
        $stdout
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            ""
            "STDERR:"
            $stderr
        }
    ) | Set-Content -Path $run.logPath -Encoding utf8

    Write-Host ""
    Write-Host "===== Integration shard $($run.number) ====="
    if (-not [string]::IsNullOrWhiteSpace($stdout)) {
        Write-Host $stdout
    }
    if (-not [string]::IsNullOrWhiteSpace($stderr)) {
        Write-Host "STDERR:"
        Write-Host $stderr
    }

    if ($run.process.ExitCode -ne 0) {
        $failedShards.Add($run.number)
    }

    $run.process.Dispose()
}

$trxFiles = @(Get-ChildItem -Path $ResultsDirectory -Filter "integration-shard-*.trx" -File)
$resultIds = [System.Collections.Generic.List[string]]::new()
$resultCount = 0

foreach ($trxFile in $trxFiles) {
    [xml] $document = Get-Content $trxFile.FullName -Raw
    $unitResults = @($document.TestRun.Results.UnitTestResult)
    $resultCount += $unitResults.Count
    foreach ($result in $unitResults) {
        $resultIds.Add([string] $result.testId)
    }
}

$uniqueResultCount = @($resultIds | Sort-Object -Unique).Count
$coverageOk =
    $trxFiles.Count -eq $ShardCount -and
    $resultCount -eq $testNames.Count -and
    $uniqueResultCount -eq $testNames.Count

$coverage = [ordered]@{
    discoveredTestCount = $testNames.Count
    shardResultFileCount = $trxFiles.Count
    resultCount = $resultCount
    uniqueResultCount = $uniqueResultCount
    coverageComplete = $coverageOk
    failedShards = @($failedShards)
}
$coveragePath = Join-Path $ShardOutputDirectory "coverage.json"
$coverage | ConvertTo-Json -Depth 4 | Set-Content -Path $coveragePath -Encoding utf8

Write-Host ""
Write-Host "Shard coverage: discovered=$($testNames.Count), results=$resultCount, unique-results=$uniqueResultCount, trx-files=$($trxFiles.Count)."

if (-not $coverageOk) {
    throw "Integration shard coverage check failed. Every discovered test must execute exactly once."
}

if ($failedShards.Count -gt 0) {
    throw "Integration shard(s) failed: $($failedShards -join ', ')."
}

Write-Host "All $ShardCount integration shards passed and covered every discovered integration test exactly once."
