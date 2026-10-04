[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [Uri]$BaseUri = "http://127.0.0.1:5080",

    # Checked in order after the dated-filename rule. The first prefix that
    # uniquely identifies one present copy becomes the keeper.
    [string[]]$PreferredPrefixes = @("fideli/"),

    [ValidateRange(1, 500)]
    [int]$BatchSize = 500,

    [string]$PlanPath = (Join-Path $PSScriptRoot "artifacts\exact-duplicate-resolution-plan.json"),

    # By default, missing source copies are not submitted for exclusion.
    [switch]$IncludeMissing,

    # Dry-run is the default. -Apply is required to change Photo Identity state.
    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$api = $BaseUri.AbsoluteUri.TrimEnd("/")

function Normalize-SourceKey {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value.Replace("\", "/").TrimStart("/")
}

function Normalize-Prefix {
    param([Parameter(Mandatory = $true)][string]$Value)

    $normalized = (Normalize-SourceKey $Value).Trim("/")
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "PreferredPrefixes cannot contain an empty prefix."
    }

    return "$normalized/"
}

function Get-FileStem {
    param([Parameter(Mandatory = $true)][string]$SourceKey)

    $normalized = Normalize-SourceKey $SourceKey
    $name = $normalized.Substring($normalized.LastIndexOf("/") + 1)
    return [System.IO.Path]::GetFileNameWithoutExtension($name)
}

function Test-DatedFileName {
    param([Parameter(Mandatory = $true)][string]$SourceKey)

    $stem = Get-FileStem $SourceKey

    # Examples:
    #   20260216_141216971_iOS.heic
    #   20260206_094157918_iOS.jpg
    # Also accepts second-resolution variants such as 20260216_141216.jpg.
    return $stem -match '^\d{8}[_-]\d{6}(?:\d{3})?(?:_iOS)?$'
}

function Test-UnderPrefix {
    param(
        [Parameter(Mandatory = $true)][string]$SourceKey,
        [Parameter(Mandatory = $true)][string]$Prefix
    )

    return (Normalize-SourceKey $SourceKey).StartsWith(
        $Prefix,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-CopySuffixName {
    param([Parameter(Mandatory = $true)][string]$SourceKey)
    return (Get-FileStem $SourceKey) -match '\(\d+\)$'
}

function Test-UuidName {
    param([Parameter(Mandatory = $true)][string]$SourceKey)
    return (Get-FileStem $SourceKey) -match '^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$'
}

function Test-NoisyFileName {
    param([Parameter(Mandatory = $true)][string]$SourceKey)
    return (Test-CopySuffixName $SourceKey) -or (Test-UuidName $SourceKey)
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

    $Plan | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $PlanPath -Encoding UTF8
}

function New-KeeperDecision {
    param(
        [Parameter(Mandatory = $true)]$Keeper,
        [Parameter(Mandatory = $true)][string]$Rule
    )

    return [pscustomobject]@{
        resolved = $true
        keeper = $Keeper
        rule = $Rule
        reason = $null
    }
}

function New-UnresolvedDecision {
    param([Parameter(Mandatory = $true)][string]$Reason)

    return [pscustomobject]@{
        resolved = $false
        keeper = $null
        rule = $null
        reason = $Reason
    }
}

function Resolve-DuplicateGroup {
    param(
        [Parameter(Mandatory = $true)][object[]]$Copies,
        [Parameter(Mandatory = $true)][string[]]$NormalizedPreferredPrefixes
    )

    # Never choose a missing source copy as the keeper when a present copy exists.
    $present = @($Copies | Where-Object { -not [bool]$_.isMissing })
    if ($present.Count -eq 0) {
        return New-UnresolvedDecision "no-present-copy"
    }

    # Rule 1: a unique dated/timestamp filename wins.
    $dated = @($present | Where-Object { Test-DatedFileName ([string]$_.sourceKey) })
    if ($dated.Count -eq 1) {
        return New-KeeperDecision $dated[0] "dated-filename"
    }
    if ($dated.Count -gt 1) {
        return New-UnresolvedDecision "multiple-dated-filenames"
    }

    # Rule 2: preferred folders, checked in user-specified order.
    foreach ($prefix in $NormalizedPreferredPrefixes) {
        $matching = @($present | Where-Object {
            Test-UnderPrefix -SourceKey ([string]$_.sourceKey) -Prefix $prefix
        })

        if ($matching.Count -eq 1) {
            return New-KeeperDecision $matching[0] "preferred-prefix:$prefix"
        }
        if ($matching.Count -gt 1) {
            return New-UnresolvedDecision "multiple-copies-under-preferred-prefix:$prefix"
        }
    }

    # Rule 3: if only one present copy has a normal-looking filename while the
    # others look like copy-suffixed or UUID-generated names, keep the clean name.
    $clean = @($present | Where-Object {
        -not (Test-NoisyFileName ([string]$_.sourceKey))
    })

    if ($clean.Count -eq 1 -and $present.Count -gt 1) {
        return New-KeeperDecision $clean[0] "clean-filename"
    }

    return New-UnresolvedDecision "no-unique-preference"
}

$normalizedPreferredPrefixes = @($PreferredPrefixes | ForEach-Object { Normalize-Prefix $_ })

Write-Host "Exact duplicate heuristic resolver"
Write-Host "  API                : $api"
Write-Host "  Preferred prefixes : $(if ($normalizedPreferredPrefixes.Count -eq 0) { '(none)' } else { $normalizedPreferredPrefixes -join ', ' })"
Write-Host "  Include missing    : $([bool]$IncludeMissing)"
Write-Host "  Mode               : $(if ($Apply) { 'APPLY' } else { 'DRY RUN' })"
Write-Host ""

$groups = @(Get-ApiArray "/api/archive/exact-duplicates")
$planGroups = @()
$targets = @()
$resolvedCount = 0
$unresolvedCount = 0
$missingTargetsSkipped = 0
$ruleCounts = @{}

for ($groupIndex = 0; $groupIndex -lt $groups.Count; $groupIndex++) {
    $copies = @($groups[$groupIndex].copies)
    if ($copies.Count -lt 2) {
        continue
    }

    $decision = Resolve-DuplicateGroup -Copies $copies -NormalizedPreferredPrefixes $normalizedPreferredPrefixes

    if (-not $decision.resolved) {
        $unresolvedCount++
        $planGroups += [pscustomobject]@{
            groupNumber = $groupIndex + 1
            status = "unresolved"
            rule = $null
            reason = $decision.reason
            keeper = $null
            exclude = @()
            copies = @($copies | ForEach-Object {
                [pscustomobject]@{
                    sourceKey = [string]$_.sourceKey
                    revisionId = [string]$_.revisionId
                    isMissing = [bool]$_.isMissing
                }
            })
        }
        continue
    }

    $resolvedCount++
    $rule = [string]$decision.rule
    if (-not $ruleCounts.ContainsKey($rule)) {
        $ruleCounts[$rule] = 0
    }
    $ruleCounts[$rule]++

    $keeper = $decision.keeper
    $excludeCopies = @($copies | Where-Object {
        [string]$_.revisionId -ne [string]$keeper.revisionId
    })

    if (-not $IncludeMissing) {
        $missingTargetsSkipped += @($excludeCopies | Where-Object { [bool]$_.isMissing }).Count
        $excludeCopies = @($excludeCopies | Where-Object { -not [bool]$_.isMissing })
    }

    $targets += $excludeCopies

    $planGroups += [pscustomobject]@{
        groupNumber = $groupIndex + 1
        status = "resolved"
        rule = $rule
        reason = $null
        keeper = [pscustomobject]@{
            sourceKey = [string]$keeper.sourceKey
            revisionId = [string]$keeper.revisionId
            isMissing = [bool]$keeper.isMissing
        }
        exclude = @($excludeCopies | ForEach-Object {
            [pscustomobject]@{
                sourceKey = [string]$_.sourceKey
                revisionId = [string]$_.revisionId
                isMissing = [bool]$_.isMissing
            }
        })
        copies = @($copies | ForEach-Object {
            [pscustomobject]@{
                sourceKey = [string]$_.sourceKey
                revisionId = [string]$_.revisionId
                isMissing = [bool]$_.isMissing
            }
        })
    }
}

$targets = @($targets | Sort-Object -Property revisionId -Unique)

$ruleSummary = [ordered]@{}
foreach ($key in @($ruleCounts.Keys | Sort-Object)) {
    $ruleSummary[$key] = [int]$ruleCounts[$key]
}

$plan = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    api = $api
    mode = if ($Apply) { "apply" } else { "dry-run" }
    preferredPrefixes = $normalizedPreferredPrefixes
    includeMissing = [bool]$IncludeMissing
    totals = [ordered]@{
        duplicateGroupsScanned = $groups.Count
        groupsAutoResolved = $resolvedCount
        groupsUnresolved = $unresolvedCount
        copiesPlannedForExclusion = $targets.Count
        missingExcludeCopiesSkipped = if ($IncludeMissing) { 0 } else { $missingTargetsSkipped }
    }
    resolvedByRule = $ruleSummary
    groups = $planGroups
}

Save-Plan $plan

Write-Host "Plan summary:"
Write-Host "  Duplicate groups scanned    : $($groups.Count)"
Write-Host "  Groups auto-resolved        : $resolvedCount"
Write-Host "  Groups left for review      : $unresolvedCount"
Write-Host "  Copies planned for exclusion: $($targets.Count)"
if (-not $IncludeMissing) {
    Write-Host "  Missing copies skipped      : $missingTargetsSkipped"
}
Write-Host "  Plan written to             : $PlanPath"
Write-Host ""

if ($ruleSummary.Count -gt 0) {
    Write-Host "Resolved by rule:"
    foreach ($entry in $ruleSummary.GetEnumerator()) {
        Write-Host "  $($entry.Key): $($entry.Value)"
    }
    Write-Host ""
}

$unresolved = @($planGroups | Where-Object { $_.status -eq "unresolved" })
if ($unresolved.Count -gt 0) {
    Write-Host "First unresolved groups (maximum 20):"
    foreach ($group in @($unresolved | Select-Object -First 20)) {
        Write-Host "  Group $($group.groupNumber): $($group.reason)"
        foreach ($copy in @($group.copies)) {
            $state = if ([bool]$copy.isMissing) { "missing" } else { "present" }
            Write-Host "    [$state] $($copy.sourceKey)"
        }
    }
    if ($unresolved.Count -gt 20) {
        Write-Host "  ...and $($unresolved.Count - 20) more unresolved group(s); see the JSON plan."
    }
    Write-Host ""
}

if (-not $Apply) {
    Write-Host "DRY RUN ONLY: no Photo Identity state was changed."
    Write-Host "Review the JSON plan, especially groups marked 'unresolved'."
    Write-Host "Re-run the same command with -Apply to submit only the auto-resolved exclusions."
    exit 0
}

if ($targets.Count -eq 0) {
    Write-Host "No auto-resolved copies are eligible for exclusion; no changes were made."
    exit 0
}

Write-Warning "Apply mode permanently purges Photo Identity data for the selected duplicate source copies."
Write-Warning "The source/OneDrive image files themselves are NOT deleted or modified."
Write-Warning "$unresolvedCount unresolved duplicate group(s) will be left untouched."

$description = "$($targets.Count) duplicate source copies from $resolvedCount auto-resolved group(s)"
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
        throw "The API reported $excludedThisBatch exclusions for a batch of $($revisionIds.Count). Stop and review Archive lifecycle before retrying."
    }

    $totalExcluded += $excludedThisBatch
    Write-Host "Applied batch: $excludedThisBatch excluded; total=$totalExcluded/$($targets.Count)"
}

# Verify that submitted revisions no longer appear in the exact-duplicate inventory.
$remainingGroups = @(Get-ApiArray "/api/archive/exact-duplicates")
$remainingRevisionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($group in $remainingGroups) {
    foreach ($copy in @($group.copies)) {
        [void]$remainingRevisionIds.Add([string]$copy.revisionId)
    }
}

$stillVisible = @($targets | Where-Object {
    $remainingRevisionIds.Contains([string]$_.revisionId)
})

Write-Host ""
Write-Host "Apply complete:"
Write-Host "  Excluded source copies : $totalExcluded"
Write-Host "  Unresolved groups      : $unresolvedCount"
Write-Host "  Submitted copies still visible as duplicates: $($stillVisible.Count)"

if ($stillVisible.Count -gt 0) {
    Write-Warning "$($stillVisible.Count) submitted copy/copies still appear in the duplicate inventory. Review Archive lifecycle before retrying."
}
else {
    Write-Host "All submitted copies disappeared from the exact-duplicate inventory as expected."
}
