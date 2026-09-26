[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [Uri]$BaseUri = "http://127.0.0.1:5080",
    [string]$KeepPrefix = "fideli/",
    [string]$ExcludePrefix = "2024/",
    [ValidateRange(1, 500)]
    [int]$BatchSize = 500,
    [string]$PlanPath = (Join-Path $PSScriptRoot "artifacts\exact-duplicate-prefix-cleanup-plan.json"),
    [switch]$IncludeMissing,
    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$api = $BaseUri.AbsoluteUri.TrimEnd("/")

function Normalize-Prefix {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [string]$ParameterName
    )

    $normalized = $Value.Trim().Replace("\", "/").Trim("/")
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "$ParameterName must name a directory below the archive root."
    }

    return "$normalized/"
}

function Normalize-KeyForMatch {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value.Replace("\", "/").TrimStart("/")
}

function Test-UnderPrefix {
    param(
        [Parameter(Mandatory = $true)][string]$SourceKey,
        [Parameter(Mandatory = $true)][string]$Prefix
    )

    return (Normalize-KeyForMatch $SourceKey).StartsWith(
        $Prefix,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-ApiGet {
    param([Parameter(Mandatory = $true)][string]$Path)
    return Invoke-RestMethod -Method Get -Uri "$api$Path"
}

function Invoke-ApiPostJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Body
    )

    $json = $Body | ConvertTo-Json -Depth 10 -Compress
    return Invoke-RestMethod `
        -Method Post `
        -Uri "$api$Path" `
        -ContentType "application/json" `
        -Body $json
}

function Get-ApiArray {
    param([Parameter(Mandatory = $true)][string]$Path)

    $response = Invoke-ApiGet $Path
    if ($null -eq $response) {
        return
    }

    foreach ($item in $response) {
        if ($null -ne $item) {
            Write-Output $item
        }
    }
}

function Save-Plan {
    param([Parameter(Mandatory = $true)]$Plan)

    $directory = Split-Path -Parent $PlanPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $Plan | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $PlanPath -Encoding UTF8
}

$keep = Normalize-Prefix -Value $KeepPrefix -ParameterName "KeepPrefix"
$exclude = Normalize-Prefix -Value $ExcludePrefix -ParameterName "ExcludePrefix"

if ($keep.Equals($exclude, [System.StringComparison]::OrdinalIgnoreCase) -or
    $keep.StartsWith($exclude, [System.StringComparison]::OrdinalIgnoreCase) -or
    $exclude.StartsWith($keep, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "KeepPrefix and ExcludePrefix must be separate, non-overlapping directory trees. Resolved values: '$keep' and '$exclude'."
}

Write-Host "Exact duplicate prefix cleanup"
Write-Host "  API            : $api"
Write-Host "  Keep prefix    : $keep"
Write-Host "  Exclude prefix : $exclude"
Write-Host "  Include missing: $([bool]$IncludeMissing)"
Write-Host "  Mode           : $(if ($Apply) { 'APPLY' } else { 'DRY RUN' })"
Write-Host ""

$groups = @(Get-ApiArray "/api/archive/exact-duplicates")
$plannedGroups = @()
$targets = @()
$groupsContainingKeep = 0
$groupsContainingKeepAndExclude = 0
$keepCopyCount = 0
$missingTargetsSkipped = 0
$otherCopiesUntouched = 0

for ($groupIndex = 0; $groupIndex -lt $groups.Count; $groupIndex++) {
    $copies = @($groups[$groupIndex].copies)
    $keepCopies = @($copies | Where-Object {
        Test-UnderPrefix -SourceKey ([string]$_.sourceKey) -Prefix $keep
    })

    if ($keepCopies.Count -eq 0) {
        continue
    }

    $groupsContainingKeep++
    $keepCopyCount += $keepCopies.Count

    $excludeCopies = @($copies | Where-Object {
        Test-UnderPrefix -SourceKey ([string]$_.sourceKey) -Prefix $exclude
    })

    if ($excludeCopies.Count -eq 0) {
        continue
    }

    $groupsContainingKeepAndExclude++

    $eligibleExcludeCopies = @($excludeCopies | Where-Object {
        $IncludeMissing -or -not [bool]$_.isMissing
    })
    $missingTargetsSkipped += @($excludeCopies | Where-Object { [bool]$_.isMissing }).Count

    $untouchedCopies = @($copies | Where-Object {
        -not (Test-UnderPrefix -SourceKey ([string]$_.sourceKey) -Prefix $keep) -and
        -not (Test-UnderPrefix -SourceKey ([string]$_.sourceKey) -Prefix $exclude)
    })
    $otherCopiesUntouched += $untouchedCopies.Count

    if ($eligibleExcludeCopies.Count -eq 0) {
        continue
    }

    $targets += $eligibleExcludeCopies
    $plannedGroups += [pscustomobject]@{
        groupNumber = $groupIndex + 1
        kept = @($keepCopies | ForEach-Object {
            [pscustomobject]@{
                sourceKey = [string]$_.sourceKey
                revisionId = [string]$_.revisionId
                isMissing = [bool]$_.isMissing
            }
        })
        excluded = @($eligibleExcludeCopies | ForEach-Object {
            [pscustomobject]@{
                sourceKey = [string]$_.sourceKey
                revisionId = [string]$_.revisionId
                isMissing = [bool]$_.isMissing
            }
        })
        untouched = @($untouchedCopies | ForEach-Object {
            [pscustomobject]@{
                sourceKey = [string]$_.sourceKey
                revisionId = [string]$_.revisionId
                isMissing = [bool]$_.isMissing
            }
        })
    }
}

$targets = @($targets | Sort-Object -Property revisionId -Unique)

$plan = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    api = $api
    keepPrefix = $keep
    excludePrefix = $exclude
    includeMissing = [bool]$IncludeMissing
    mode = if ($Apply) { "apply" } else { "dry-run" }
    totals = [ordered]@{
        duplicateGroupsScanned = $groups.Count
        groupsContainingKeepPrefix = $groupsContainingKeep
        groupsContainingKeepAndExcludePrefixes = $groupsContainingKeepAndExclude
        keepCopiesMatched = $keepCopyCount
        copiesPlannedForExclusion = $targets.Count
        missingExcludeCopiesSkipped = if ($IncludeMissing) { 0 } else { $missingTargetsSkipped }
        otherPrefixCopiesUntouched = $otherCopiesUntouched
    }
    groups = $plannedGroups
}

Save-Plan $plan

Write-Host "Plan summary:"
Write-Host "  Duplicate groups scanned       : $($groups.Count)"
Write-Host "  Groups containing '$keep'      : $groupsContainingKeep"
Write-Host "  Groups containing both prefixes: $groupsContainingKeepAndExclude"
Write-Host "  Keep-prefix copies matched     : $keepCopyCount"
Write-Host "  Copies planned for exclusion   : $($targets.Count)"
if (-not $IncludeMissing) {
    Write-Host "  Missing exclude copies skipped : $missingTargetsSkipped"
}
Write-Host "  Other-prefix copies untouched  : $otherCopiesUntouched"
Write-Host "  Plan written to                : $PlanPath"
Write-Host ""

if ($targets.Count -gt 0) {
    Write-Host "Copies selected for exclusion:"
    foreach ($target in $targets) {
        $state = if ([bool]$target.isMissing) { "missing" } else { "present" }
        Write-Host "  [$state] $([string]$target.sourceKey)"
    }
    Write-Host ""
}

if (-not $Apply) {
    Write-Host "DRY RUN ONLY: no catalogue state was changed."
    Write-Host "Review the plan and paths above. Re-run with -Apply to exclude and purge the selected '$exclude' copies."
    exit 0
}

if ($targets.Count -eq 0) {
    Write-Host "Nothing matches the cleanup rule; no changes were made."
    exit 0
}

Write-Warning "Apply mode permanently purges Photo Identity data for the selected source copies. The OneDrive/source originals are NOT deleted."
Write-Warning "Copies under '$keep' and copies outside '$exclude' remain included."

$description = "$($targets.Count) exact-duplicate source copies under '$exclude' that have a sibling under '$keep'"
if (-not $PSCmdlet.ShouldProcess($description, "Exclude and purge from Photo Identity")) {
    Write-Host "Apply cancelled; no exclusions were submitted."
    exit 0
}

$totalExcluded = 0
for ($offset = 0; $offset -lt $targets.Count; $offset += $BatchSize) {
    $batch = @($targets | Select-Object -Skip $offset -First $BatchSize)
    $revisionIds = @($batch | ForEach-Object { [string]$_.revisionId })

    $response = Invoke-ApiPostJson "/api/archive/exclusions/revisions" @{
        revisionIds = $revisionIds
    }

    $excludedThisBatch = [int]$response.excluded
    if ($excludedThisBatch -ne $revisionIds.Count) {
        throw "The API reported $excludedThisBatch exclusions for a batch of $($revisionIds.Count). Stop and review lifecycle state before continuing."
    }

    $totalExcluded += $excludedThisBatch
    Write-Host "Applied batch: $excludedThisBatch excluded; total=$totalExcluded/$($targets.Count)"
}

$exclusions = @(Get-ApiArray "/api/archive/exclusions")
$exclusionKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($item in $exclusions) {
    [void]$exclusionKeys.Add("$([string]$item.sourceId)`n$([string]$item.sourceKey)")
}

$missingExclusionRecords = @($targets | Where-Object {
    -not $exclusionKeys.Contains("$([string]$_.sourceId)`n$([string]$_.sourceKey)")
})
if ($missingExclusionRecords.Count -gt 0) {
    throw "$($missingExclusionRecords.Count) selected copies were not visible in the exclusion inventory after apply. Review /archive/lifecycle before retrying."
}

$remainingGroups = @(Get-ApiArray "/api/archive/exact-duplicates")
$remainingRevisionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($copy in @($remainingGroups | ForEach-Object { @($_.copies) })) {
    [void]$remainingRevisionIds.Add([string]$copy.revisionId)
}

$stillVisible = @($targets | Where-Object {
    $remainingRevisionIds.Contains([string]$_.revisionId)
})
if ($stillVisible.Count -gt 0) {
    throw "$($stillVisible.Count) excluded copies still appear in exact-duplicate inventory. Review lifecycle state before retrying."
}

$targetLocators = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($target in $targets) {
    [void]$targetLocators.Add("$([string]$target.sourceId)`n$([string]$target.sourceKey)")
}
$targetExclusions = @($exclusions | Where-Object {
    $targetLocators.Contains("$([string]$_.sourceId)`n$([string]$_.sourceKey)")
})
$pending = @($targetExclusions | Where-Object { $_.purgeState -in @("pending", "attempting") }).Count
$failed = @($targetExclusions | Where-Object { $_.purgeState -eq "failed" }).Count
$completed = @($targetExclusions | Where-Object { $_.purgeState -eq "completed" }).Count

Write-Host ""
Write-Host "Apply complete:"
Write-Host "  Excluded source copies : $totalExcluded"
Write-Host "  Purge completed        : $completed"
Write-Host "  Purge pending/running  : $pending"
Write-Host "  Purge failed           : $failed"
Write-Host ""
Write-Host "All selected copies are now blocked from normal Photo Identity access. Pending purge work may continue asynchronously in the application."
if ($failed -gt 0) {
    Write-Warning "$failed purge operation(s) failed. Use Archive lifecycle -> Purge failed to retry them."
}
