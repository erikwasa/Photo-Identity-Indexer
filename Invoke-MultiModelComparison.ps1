<#
.SYNOPSIS
Runs the repeatable WI-0030 comparison steps from one JSON configuration.

.DESCRIPTION
Compatible with Windows PowerShell 5.1 and PowerShell 7. The configured workspace
must remain outside the source tree and must not be committed.
#>
[CmdletBinding()]
param(
    [string] $ConfigPath,
    [switch] $InstallModels,
    [switch] $RunPreflight,
    [switch] $Resume,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RelativePathCompat([string] $BasePath, [string] $TargetPath) {
    $separator = [IO.Path]::DirectorySeparatorChar
    $base = [IO.Path]::GetFullPath($BasePath)
    if (-not $base.EndsWith([string]$separator, [StringComparison]::Ordinal)) {
        $base += $separator
    }

    $baseUri = New-Object Uri -ArgumentList $base
    $targetUri = New-Object Uri -ArgumentList ([IO.Path]::GetFullPath($TargetPath))
    $value = [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString())
    return $value.Replace("/", [string]$separator)
}

function Read-Json([string] $Path) {
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Write-Json([object] $Value, [string] $Path) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Get-Sha256([string] $Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Snapshot([string] $Root) {
    @(
        Get-ChildItem -LiteralPath $Root -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                [pscustomobject]@{
                    relativePath = (Get-RelativePathCompat $Root $_.FullName).Replace("\", "/")
                    length = [int64]$_.Length
                    sha256 = Get-Sha256 $_.FullName
                }
            }
    )
}

function Get-Signature([object[]] $Items, [scriptblock] $Selector) {
    @($Items | ForEach-Object $Selector | Sort-Object)
}

function Assert-Same([string] $Name, [object[]] $Expected, [object[]] $Actual) {
    if (@(Compare-Object $Expected $Actual).Count -ne 0) {
        throw "$Name differs."
    }
}

function Invoke-Logged([string[]] $Arguments, [string] $LogPath, [string] $Description) {
    Write-Host "`n==> $Description"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $lines = @(& dotnet @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $timer.Stop()

    New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force | Out-Null
    $lines | Set-Content -LiteralPath $LogPath -Encoding UTF8
    $lines | ForEach-Object { Write-Host $_ }

    if ($exitCode -ne 0) {
        throw "$Description failed. See $LogPath"
    }

    [pscustomobject]@{
        lines = $lines
        elapsedSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
    }
}

function Convert-OutputMap([object[]] $Lines) {
    $map = [ordered]@{}
    foreach ($line in $Lines) {
        if ([string]$line -match "^([^:]+):\s*(.*)$") {
            $map[$matches[1].Trim()] = $matches[2].Trim()
        }
    }
    return $map
}

function Read-Manifest([string] $ModelId, [string] $Role) {
    $path = Join-Path $PSScriptRoot "models\manifests\$ModelId.json"
    $manifest = Read-Json $path
    if ([string]$manifest.role -ne $Role) {
        throw "Model '$ModelId' must have role '$Role'."
    }
    return $manifest
}

function Assert-Model([object] $Manifest) {
    $path = Join-Path $PSScriptRoot "models\files\$($Manifest.fileName)"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Model file is missing: $path"
    }
    if ((Get-Item -LiteralPath $path).Length -ne [int64]$Manifest.sizeBytes) {
        throw "Model size mismatch for '$($Manifest.modelId)'."
    }
    if ((Get-Sha256 $path) -ne [string]$Manifest.sha256) {
        throw "Model hash mismatch for '$($Manifest.modelId)'."
    }
}

function Copy-Catalogue([string] $Source, [string] $Destination) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    foreach ($suffix in @("", "-wal", "-shm")) {
        if (Test-Path -LiteralPath "$Source$suffix" -PathType Leaf) {
            Copy-Item -LiteralPath "$Source$suffix" -Destination "$Destination$suffix" -Force
        }
    }
}

function Invoke-SelfTest {
    $root = Join-Path ([IO.Path]::GetTempPath()) "photo-identity-$([Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path (Join-Path $root "nested") -Force | Out-Null
        $file = Join-Path $root "nested\sample #1.txt"
        Set-Content -LiteralPath $file -Value "test" -Encoding UTF8

        $expected = "nested$([IO.Path]::DirectorySeparatorChar)sample #1.txt"
        if ((Get-RelativePathCompat $root $file) -ne $expected) {
            throw "Relative-path compatibility test failed."
        }
        if ((Get-Snapshot $root)[0].relativePath -ne "nested/sample #1.txt") {
            throw "Snapshot compatibility test failed."
        }
        Write-Host "Self-test passed on PowerShell $($PSVersionTable.PSVersion)."
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    throw "-ConfigPath is required."
}

$configPath = [IO.Path]::GetFullPath($ConfigPath)
$config = Read-Json $configPath
$configDirectory = Split-Path -Parent $configPath

function Resolve-Configured([string] $Value) {
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    return [IO.Path]::GetFullPath((Join-Path $configDirectory $Value))
}

$source = Resolve-Configured ([string]$config.sourcePath)
$database = Resolve-Configured ([string]$config.databasePath)
$workspace = Resolve-Configured ([string]$config.workspacePath)
$models = @($config.models)
$evaluation = $config.evaluation

if ($models.Count -lt 2) { throw "Configure at least two models." }
$sourcePrefix = $source.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($workspace.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "workspacePath must not be inside sourcePath."
}
if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw "Missing sourcePath: $source" }
if (-not (Test-Path -LiteralPath $database -PathType Leaf)) { throw "Missing databasePath: $database" }

New-Item -ItemType Directory -Path $workspace -Force | Out-Null

$detector = Read-Manifest ([string]$config.detectorModelId) "detector"
$resolved = @(
    foreach ($item in $models) {
        $manifest = Read-Manifest ([string]$item.modelId) "embedder"
        [pscustomobject]@{
            name = [string]$item.name
            manifest = $manifest
            root = Join-Path $workspace "models\$($item.name)"
            output = Join-Path $workspace "models\$($item.name)\output"
        }
    }
)

if ($InstallModels) {
    foreach ($id in @([string]$detector.modelId) + @($resolved | ForEach-Object { [string]$_.manifest.modelId })) {
        & (Join-Path $PSScriptRoot "models\install-models.ps1") -Id $id
        if (-not $?) { throw "Model installation failed for '$id'." }
    }
}

Assert-Model $detector
foreach ($model in $resolved) { Assert-Model $model.manifest }

if ($RunPreflight) {
    & (Join-Path $PSScriptRoot "verify-local.ps1") -InstallModels
    if (-not $?) { throw "Local verification failed." }
    & (Join-Path $PSScriptRoot "verify-review.ps1") -Mode Smoke -Configuration Release
    if (-not $?) { throw "Review verification failed." }
}

$snapshotPath = Join-Path $workspace "source-snapshot.json"
$snapshot = Get-Snapshot $source
if ((Test-Path -LiteralPath $snapshotPath) -and $Resume) {
    $saved = @(Read-Json $snapshotPath)
    Assert-Same "Source snapshot" `
        (Get-Signature $saved { "$($_.relativePath)|$($_.length)|$($_.sha256)" }) `
        (Get-Signature $snapshot { "$($_.relativePath)|$($_.length)|$($_.sha256)" })
}
else {
    Write-Json $snapshot $snapshotPath
    Copy-Catalogue $database (Join-Path $workspace "backup\catalogue.db")
}

$referenceResults = $null
$referenceSplit = $null
$summaries = @()

foreach ($model in $resolved) {
    New-Item -ItemType Directory -Path $model.root -Force | Out-Null
    $statePath = Join-Path $model.root "state.json"

    if ((Test-Path -LiteralPath $statePath) -and $Resume) {
        $state = Read-Json $statePath
    }
    else {
        $batch = Invoke-Logged @(
            "run", "--project", ".\src\PhotoIdentity.Cli", "--",
            "batch", "start",
            "--database", $database,
            "--source", $source,
            "--output", $model.output,
            "--detector-model", [string]$detector.modelId,
            "--embedder-model", [string]$model.manifest.modelId
        ) (Join-Path $model.root "batch.log") "Process $($model.name)"

        $batchMap = Convert-OutputMap $batch.lines
        $runId = [string]$batchMap["run"]
        if ([string]$batchMap["status"] -ne "completed" -or [int]$batchMap["failed"] -ne 0) {
            throw "Processing failed for '$($model.name)'."
        }
        if ([bool]$config.requireExistingSourceRevisions -and [int]$batchMap["scan-new-revisions"] -ne 0) {
            throw "New source revisions were found for '$($model.name)'."
        }

        $match = @()
        foreach ($attempt in 1, 2) {
            $result = Invoke-Logged @(
                "run", "--project", ".\src\PhotoIdentity.Cli", "--",
                "match", "regenerate",
                "--database", $database,
                "--embedder-id", [string]$model.manifest.modelId,
                "--embedder-hash", [string]$model.manifest.sha256
            ) (Join-Path $model.root "match-$attempt.log") "Match $($model.name), attempt $attempt"
            $match += [pscustomobject](Convert-OutputMap $result.lines)
        }
        if (($match[0] | ConvertTo-Json -Compress) -ne ($match[1] | ConvertTo-Json -Compress)) {
            throw "Suggestion regeneration is unstable for '$($model.name)'."
        }

        $manifestPath = Join-Path $model.root "evaluation-dataset.json"
        $reportPath = Join-Path $model.root "evaluation-report.json"
        $export = @(
            "run", "--project", ".\src\PhotoIdentity.Cli", "--",
            "evaluate", "export",
            "--database", $database,
            "--output", $manifestPath,
            "--dataset-id", [string]$evaluation.datasetId,
            "--pipeline-version", [string]$evaluation.pipelineVersion,
            "--detector-id", [string]$detector.modelId,
            "--detector-hash", [string]$detector.sha256,
            "--embedder-id", [string]$model.manifest.modelId,
            "--embedder-hash", [string]$model.manifest.sha256,
            "--seed", [string]$evaluation.seed,
            "--run", $runId,
            "--gallery-per-person", [string]$evaluation.galleryPerPerson,
            "--validation-known-per-person", [string]$evaluation.validationKnownPerPerson,
            "--test-known-per-person", [string]$evaluation.testKnownPerPerson,
            "--validation-unknown", [string]$evaluation.validationUnknown,
            "--test-unknown", [string]$evaluation.testUnknown
        )
        foreach ($threshold in @($evaluation.thresholds)) {
            $export += @("--threshold", ([double]$threshold).ToString("R", [Globalization.CultureInfo]::InvariantCulture))
        }

        Invoke-Logged $export (Join-Path $model.root "export-1.log") "Export $($model.name), first" | Out-Null
        $manifestHash = Get-Sha256 $manifestPath
        Invoke-Logged $export (Join-Path $model.root "export-2.log") "Export $($model.name), second" | Out-Null
        if ($manifestHash -ne (Get-Sha256 $manifestPath)) { throw "Nondeterministic export for '$($model.name)'." }

        $evaluate = @(
            "run", "--project", ".\src\PhotoIdentity.Cli", "--",
            "evaluate", "--dataset", $manifestPath, "--output", $reportPath
        )
        Invoke-Logged $evaluate (Join-Path $model.root "evaluate-1.log") "Evaluate $($model.name), first" | Out-Null
        $reportHash = Get-Sha256 $reportPath
        Invoke-Logged $evaluate (Join-Path $model.root "evaluate-2.log") "Evaluate $($model.name), second" | Out-Null
        if ($reportHash -ne (Get-Sha256 $reportPath)) { throw "Nondeterministic report for '$($model.name)'." }

        $state = [ordered]@{
            runId = $runId
            processingSeconds = $batch.elapsedSeconds
            suggestions = $match[1]
            manifestSha256 = $manifestHash
            reportSha256 = $reportHash
        }
        Write-Json $state $statePath
    }

    $results = @(
        Get-ChildItem -LiteralPath (Join-Path $model.output "runs\$($state.runId)") -Filter result.json -File -Recurse |
            ForEach-Object {
                $value = Read-Json $_.FullName
                [pscustomobject]@{
                    revision = [string]$value.assetRevisionId
                    sourceHash = [string]$value.sourceSha256
                    faces = [int]$value.faceCount
                }
            } |
            Sort-Object revision
    )

    if ($null -eq $referenceResults) {
        $referenceResults = $results
    }
    else {
        Assert-Same "Processing scope" `
            (Get-Signature $referenceResults { "$($_.revision)|$($_.sourceHash)" }) `
            (Get-Signature $results { "$($_.revision)|$($_.sourceHash)" })
        Assert-Same "Detector counts" `
            (Get-Signature $referenceResults { "$($_.revision)|$($_.faces)" }) `
            (Get-Signature $results { "$($_.revision)|$($_.faces)" })
    }

    $manifest = Read-Json (Join-Path $model.root "evaluation-dataset.json")
    if ([bool]$config.requireMeasuredTiming -and
        [int]$manifest.catalogueExport.fallbackTimingSampleCount -ne 0) {
        throw "Timing fallback was used for '$($model.name)'; throughput is not comparable."
    }

    $split = [pscustomobject]@{
        source = Get-Signature @($manifest.catalogueExport.sourceRevisions) { "$($_.assetRevisionId)|$($_.contentSha256)" }
        gallery = Get-Signature @($manifest.gallery) { "$($_.sourceRevisionId)|$($_.faceId)|$($_.personId)" }
        validation = Get-Signature @($manifest.validation) { "$($_.sourceRevisionId)|$($_.faceId)|$($_.expectedPersonId)" }
        test = Get-Signature @($manifest.test) { "$($_.sourceRevisionId)|$($_.faceId)|$($_.expectedPersonId)" }
    }

    if ($null -eq $referenceSplit) {
        $referenceSplit = $split
    }
    else {
        Assert-Same "Evaluation source" $referenceSplit.source $split.source
        Assert-Same "Evaluation gallery" $referenceSplit.gallery $split.gallery
        Assert-Same "Evaluation validation" $referenceSplit.validation $split.validation
        Assert-Same "Evaluation test" $referenceSplit.test $split.test
    }

    $report = Read-Json (Join-Path $model.root "evaluation-report.json")
    $summaries += [ordered]@{
        name = $model.name
        modelId = [string]$model.manifest.modelId
        modelHash = [string]$model.manifest.sha256
        modelBytes = [int64]$model.manifest.sizeBytes
        runId = [string]$state.runId
        revisions = $results.Count
        faces = [int](($results | Measure-Object faces -Sum).Sum)
        processingSeconds = [double]$state.processingSeconds
        outputBytes = Get-DirectoryBytes $model.output
        suggestions = $state.suggestions
        selectedThreshold = [double]$report.selectedThreshold
        validation = $report.validation.metrics
        test = $report.test.metrics
    }
}

Assert-Same "Source after comparison" `
    (Get-Signature @(Read-Json $snapshotPath) { "$($_.relativePath)|$($_.length)|$($_.sha256)" }) `
    (Get-Signature (Get-Snapshot $source) { "$($_.relativePath)|$($_.length)|$($_.sha256)" })

$summaryPath = Join-Path $workspace "comparison-summary.json"
Write-Json ([ordered]@{
    schemaVersion = 1
    workItem = "WI-0030"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    sourceFiles = $snapshot.Count
    detector = [ordered]@{ modelId = $detector.modelId; modelHash = $detector.sha256 }
    sameImmutableScope = $true
    sameDetectorCounts = $true
    sameEvaluationSplit = $true
    models = $summaries
    remainingManualGates = @(
        "Windows and Pixel exact-model UI verification",
        "Canonical review-state comparison",
        "Representative disagreement review",
        "Recommendation and uncertainty"
    )
}) $summaryPath

$manualPath = Join-Path $workspace "manual-verification.md"
@"
# WI-0030 manual verification

Automated comparison passed. Publish the review application against:

`$database`

Then verify every exact model revision on Windows and Pixel, confirm model switching
does not change people, assignments or audit history, review representative
disagreements, and record a privacy-safe recommendation and remaining uncertainty.

Do not commit this workspace or any private paths, IDs, manifests, reports or data.
"@ | Set-Content -LiteralPath $manualPath -Encoding UTF8

Write-Host "`nComparison completed."
Write-Host "Summary: $summaryPath"
Write-Host "Manual checklist: $manualPath"
