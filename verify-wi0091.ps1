[CmdletBinding()]
param(
    [ValidateSet(
        "Preflight",
        "Sync",
        "ListDuplicates",
        "RecordDuplicate",
        "VerifyDuplicate",
        "ListRemoved",
        "RecordRemoved",
        "VerifyRemoved",
        "RecordPhoto",
        "VerifyPhotoRemoved",
        "VerifyPhotoExclusion",
        "WaitForPurge",
        "VerifyRestore",
        "VerifyExcludedRename")]
    [string]$Stage = "Preflight",

    [Uri]$BaseUri = "http://127.0.0.1:5080",
    [string]$StatePath = (Join-Path $PSScriptRoot "artifacts\wi-0091-verification-state.json"),

    [int]$DuplicateGroup,
    [int]$DuplicateCopy = 1,
    [int[]]$RemovedRows,
    [string]$Photo,
    [string]$NewSourceKey,

    [ValidateSet("duplicate", "removed", "photo", "all")]
    [string]$Target = "all",
    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 300,

    [switch]$RunPostgres,
    [switch]$ShowPrivateDetails
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$api = $BaseUri.AbsoluteUri.TrimEnd("/")

function Write-Pass {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "PASS: $Message"
}

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw "VERIFY FAILED: $Message"
    }

    Write-Pass $Message
}

function Invoke-ApiGet {
    param([Parameter(Mandatory = $true)][string]$Path)
    return Invoke-RestMethod -Method Get -Uri "$api$Path"
}

function Invoke-ApiPost {
    param([Parameter(Mandatory = $true)][string]$Path)
    return Invoke-RestMethod -Method Post -Uri "$api$Path"
}

function Get-HttpStatus {
    param([Parameter(Mandatory = $true)][string]$Path)

    $client = [System.Net.Http.HttpClient]::new()
    try {
        $response = $client.GetAsync("$api$Path").GetAwaiter().GetResult()
        try {
            return [int]$response.StatusCode
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }
}

function Get-Exclusions {
    return @(Invoke-ApiGet "/api/archive/exclusions")
}

function Get-DuplicateGroups {
    return @(Invoke-ApiGet "/api/archive/exact-duplicates")
}

function Get-ArchiveItems {
    param(
        [string]$Analysis = "all",
        [int]$PageSize = 200
    )

    $effectivePageSize = [Math]::Min(200, [Math]::Max(1, $PageSize))
    $items = @()
    $offset = 0
    do {
        $path = "/api/archive/items/filter?availability=all&verification=all&analysis=$([Uri]::EscapeDataString($Analysis))&folder=&offset=$offset&limit=$effectivePageSize"
        $page = Invoke-ApiGet $path
        $pageItems = @($page.items)
        $items += $pageItems
        $offset += $pageItems.Count
        $total = [int]$page.total
    } while ($offset -lt $total -and $pageItems.Count -gt 0)

    return $items
}

function Get-RemovedItems {
    return @(Get-ArchiveItems -Analysis "missing")
}

function Resolve-RevisionId {
    param([Parameter(Mandatory = $true)][string]$Value)

    $match = [regex]::Match(
        $Value,
        "(?i)([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})")
    if (-not $match.Success) {
        throw "Could not find a revision GUID in -Photo. Pass either the revision GUID or the full /photo/<revision> URL."
    }

    return $match.Groups[1].Value.ToLowerInvariant()
}

function Get-State {
    if (Test-Path -LiteralPath $StatePath -PathType Leaf) {
        return Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json -AsHashtable
    }

    return [ordered]@{
        schemaVersion = 1
        capturedAtUtc = $null
        duplicate = $null
        removed = @()
        photo = $null
    }
}

function Save-State {
    param([Parameter(Mandatory = $true)]$State)

    $directory = Split-Path -Parent $StatePath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $State["capturedAtUtc"] = [DateTimeOffset]::UtcNow.ToString("O")
    $State | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $StatePath -Encoding UTF8
    Write-Host "Verification state saved locally under artifacts/."
}

function Get-MatchingExclusion {
    param(
        [Parameter(Mandatory = $true)]$Exclusions,
        [string]$SourceId,
        [Parameter(Mandatory = $true)][string]$SourceKey
    )

    return @($Exclusions | Where-Object {
        $_.sourceKey -eq $SourceKey -and
        ([string]::IsNullOrWhiteSpace($SourceId) -or $_.sourceId -eq $SourceId)
    }) | Select-Object -First 1
}

function Get-TargetLocators {
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)][string]$RequestedTarget
    )

    $values = @()
    if ($RequestedTarget -in @("duplicate", "all") -and $null -ne $State["duplicate"]) {
        $values += [pscustomobject]@{
            kind = "duplicate"
            sourceId = [string]$State["duplicate"]["target"]["sourceId"]
            sourceKey = [string]$State["duplicate"]["target"]["sourceKey"]
        }
    }

    if ($RequestedTarget -in @("removed", "all")) {
        foreach ($item in @($State["removed"])) {
            $values += [pscustomobject]@{
                kind = "removed"
                sourceId = ""
                sourceKey = [string]$item["sourceKey"]
            }
        }
    }

    if ($RequestedTarget -in @("photo", "all") -and $null -ne $State["photo"] -and $State["photo"].ContainsKey("sourceKey")) {
        $values += [pscustomobject]@{
            kind = "photo"
            sourceId = if ($State["photo"].ContainsKey("sourceId")) { [string]$State["photo"]["sourceId"] } else { "" }
            sourceKey = [string]$State["photo"]["sourceKey"]
        }
    }

    return $values
}

function Show-PrivateValue {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($ShowPrivateDetails) {
        Write-Host "    $Value"
    }
}

switch ($Stage) {
    "Preflight" {
        if ($RunPostgres) {
            Write-Host "Running live PostgreSQL verification first..."
            & (Join-Path $PSScriptRoot "verify-postgres.ps1") -SkipContainerStart
            if ($LASTEXITCODE -ne 0) {
                throw "verify-postgres.ps1 failed with exit code $LASTEXITCODE."
            }
            Write-Pass "live PostgreSQL verification"
        }

        $healthStatus = Get-HttpStatus "/health"
        Assert-Condition ($healthStatus -eq 200) "application health endpoint is ready"

        $status = Invoke-ApiGet "/api/archive/status"
        Assert-Condition ([bool]$status.configured) "permanent archive is configured"

        $removed = @(Get-RemovedItems)
        $groups = @(Get-DuplicateGroups)
        $copies = @($groups | ForEach-Object { @($_.copies) }).Count
        $exclusions = @(Get-Exclusions)
        $pending = @($exclusions | Where-Object { $_.purgeState -in @("pending", "attempting") }).Count
        $failed = @($exclusions | Where-Object { $_.purgeState -eq "failed" }).Count
        $completed = @($exclusions | Where-Object { $_.purgeState -eq "completed" }).Count

        Write-Host "WI-0091 preflight counts:"
        Write-Host "  Removed from source : $($removed.Count)"
        Write-Host "  Duplicate groups    : $($groups.Count)"
        Write-Host "  Duplicate copies    : $copies"
        Write-Host "  Purge pending       : $pending"
        Write-Host "  Purge failed        : $failed"
        Write-Host "  Excluded/completed  : $completed"
        Write-Pass "lifecycle APIs responded without exposing private details in this summary"
    }

    "Sync" {
        $sync = Invoke-ApiPost "/api/archive/sync"
        Write-Host "Archive sync completed:"
        Write-Host "  New revisions  : $($sync.newRevisions)"
        Write-Host "  Unchanged files: $($sync.unchangedFiles)"
        Write-Host "  Marked missing : $($sync.markedMissing)"
        Write-Pass "archive synchronization"
    }

    "ListDuplicates" {
        $groups = @(Get-DuplicateGroups)
        if ($groups.Count -eq 0) {
            Write-Host "No exact duplicate groups are currently available."
            break
        }

        for ($groupIndex = 0; $groupIndex -lt $groups.Count; $groupIndex++) {
            $copies = @($groups[$groupIndex].copies)
            $present = @($copies | Where-Object { -not $_.isMissing }).Count
            $missing = $copies.Count - $present
            Write-Host "Group $($groupIndex + 1): $($copies.Count) copies; present=$present removed=$missing"
            for ($copyIndex = 0; $copyIndex -lt $copies.Count; $copyIndex++) {
                $copy = $copies[$copyIndex]
                $copyState = if ($copy.isMissing) { "removed" } else { "present" }
                Write-Host "  Copy $($copyIndex + 1): $copyState"
                Show-PrivateValue ([string]$copy.sourceKey)
            }
        }

        Write-Host "Use the same group/copy numbers with -Stage RecordDuplicate."
    }

    "RecordDuplicate" {
        Assert-Condition ($DuplicateGroup -ge 1) "duplicate group number was supplied"
        Assert-Condition ($DuplicateCopy -ge 1) "duplicate copy number was supplied"

        $groups = @(Get-DuplicateGroups)
        Assert-Condition ($DuplicateGroup -le $groups.Count) "duplicate group exists"
        $group = $groups[$DuplicateGroup - 1]
        $copies = @($group.copies)
        Assert-Condition ($copies.Count -ge 2) "selected group still contains at least two copies"
        Assert-Condition ($DuplicateCopy -le $copies.Count) "selected duplicate copy exists"

        $target = $copies[$DuplicateCopy - 1]
        $sibling = @($copies | Where-Object { $_.revisionId -ne $target.revisionId }) | Select-Object -First 1
        Assert-Condition ($null -ne $sibling) "an independently catalogued sibling copy exists"

        $state = Get-State
        $state["duplicate"] = [ordered]@{
            groupNumber = $DuplicateGroup
            target = [ordered]@{
                sourceId = [string]$target.sourceId
                sourceKey = [string]$target.sourceKey
                assetId = [string]$target.assetId
                revisionId = [string]$target.revisionId
            }
            sibling = [ordered]@{
                sourceId = [string]$sibling.sourceId
                sourceKey = [string]$sibling.sourceKey
                assetId = [string]$sibling.assetId
                revisionId = [string]$sibling.revisionId
            }
        }
        Save-State $state
        Write-Pass "duplicate A/B pair recorded without changing catalogue state"
        if ($ShowPrivateDetails) {
            Write-Host "Target source copy:"
            Show-PrivateValue ([string]$target.sourceKey)
            Write-Host "Sibling source copy:"
            Show-PrivateValue ([string]$sibling.sourceKey)
        }
    }

    "VerifyDuplicate" {
        $state = Get-State
        Assert-Condition ($null -ne $state["duplicate"]) "duplicate baseline is available"
        $target = $state["duplicate"]["target"]
        $sibling = $state["duplicate"]["sibling"]
        $exclusions = @(Get-Exclusions)

        $targetExclusion = Get-MatchingExclusion -Exclusions $exclusions -SourceId ([string]$target["sourceId"]) -SourceKey ([string]$target["sourceKey"])
        $siblingExclusion = Get-MatchingExclusion -Exclusions $exclusions -SourceId ([string]$sibling["sourceId"]) -SourceKey ([string]$sibling["sourceKey"])
        Assert-Condition ($null -ne $targetExclusion) "duplicate A is excluded"
        Assert-Condition ($null -eq $siblingExclusion) "duplicate B remains included"

        $groups = @(Get-DuplicateGroups)
        $visibleRevisionIds = @($groups | ForEach-Object { @($_.copies) } | ForEach-Object { [string]$_.revisionId })
        Assert-Condition ($visibleRevisionIds -notcontains [string]$target["revisionId"]) "excluded duplicate A immediately leaves duplicate review"

        $previewStatus = Get-HttpStatus "/api/collections/photos/$([Uri]::EscapeDataString([string]$target["revisionId"]))/viewer-preview"
        Assert-Condition ($previewStatus -lt 200 -or $previewStatus -ge 300) "excluded duplicate A is blocked from normal viewer-preview access"
        Write-Host "Current purge state for duplicate A: $($targetExclusion.purgeState)"
    }

    "ListRemoved" {
        $removed = @(Get-RemovedItems)
        Write-Host "Removed-source entries: $($removed.Count)"
        for ($index = 0; $index -lt $removed.Count; $index++) {
            $item = $removed[$index]
            $revisionState = if ([string]::IsNullOrWhiteSpace([string]$item.revisionId)) { "missing" } else { "available" }
            Write-Host "  Row $($index + 1): revision=$revisionState"
            Show-PrivateValue ([string]$item.relativePath)
        }
        Write-Host "Choose safe row numbers and use -Stage RecordRemoved -RemovedRows <n1>,<n2>."
    }

    "RecordRemoved" {
        Assert-Condition ($null -ne $RemovedRows -and $RemovedRows.Count -gt 0) "at least one removed row was supplied"
        $removed = @(Get-RemovedItems)
        $selected = @()
        foreach ($row in $RemovedRows) {
            Assert-Condition ($row -ge 1 -and $row -le $removed.Count) "removed row $row exists"
            $item = $removed[$row - 1]
            Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$item.revisionId)) "removed row $row retains a revision for review/exclusion"
            $selected += [ordered]@{
                revisionId = [string]$item.revisionId
                sourceKey = [string]$item.relativePath
            }
        }

        $state = Get-State
        $state["removed"] = $selected
        Save-State $state
        Write-Pass "$($selected.Count) removed-source entries recorded for bulk verification"
    }

    "VerifyRemoved" {
        $state = Get-State
        $recorded = @($state["removed"])
        Assert-Condition ($recorded.Count -gt 0) "removed-source baseline is available"

        $exclusions = @(Get-Exclusions)
        $remainingRemoved = @(Get-RemovedItems)
        foreach ($item in $recorded) {
            $matchingExclusion = Get-MatchingExclusion -Exclusions $exclusions -SourceId "" -SourceKey ([string]$item["sourceKey"])
            Assert-Condition ($null -ne $matchingExclusion) "recorded removed source copy has a durable exclusion"
            $stillRemoved = @($remainingRemoved | Where-Object { $_.relativePath -eq [string]$item["sourceKey"] }).Count -gt 0
            Assert-Condition (-not $stillRemoved) "excluded source copy immediately leaves Removed from source"
            $previewStatus = Get-HttpStatus "/api/collections/photos/$([Uri]::EscapeDataString([string]$item["revisionId"]))/viewer-preview"
            Assert-Condition ($previewStatus -lt 200 -or $previewStatus -ge 300) "excluded removed-source revision is blocked from viewer-preview access"
        }
        Write-Pass "all recorded removed entries are in exclusion/purge state"
    }

    "RecordPhoto" {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($Photo)) "photo revision or URL was supplied"
        $revisionId = Resolve-RevisionId $Photo
        $items = @(Get-ArchiveItems -Analysis "all")
        $item = @($items | Where-Object { [string]$_.revisionId -eq $revisionId }) | Select-Object -First 1
        Assert-Condition ($null -ne $item) "photo revision belongs to the configured archive"
        Assert-Condition ([string]$item.analysisState -ne "missing") "recorded photo is still present in the source"

        $state = Get-State
        $state["photo"] = [ordered]@{
            revisionId = $revisionId
            sourceKey = [string]$item.relativePath
            previewStatusBefore = Get-HttpStatus "/api/collections/photos/$([Uri]::EscapeDataString($revisionId))/viewer-preview"
        }
        Save-State $state
        Write-Pass "still-present photo baseline recorded without changing catalogue state"
        Show-PrivateValue ([string]$item.relativePath)
    }

    "VerifyPhotoRemoved" {
        $state = Get-State
        Assert-Condition ($null -ne $state["photo"]) "photo baseline is available"
        $removed = @(Get-RemovedItems)
        $match = @($removed | Where-Object { $_.relativePath -eq [string]$state["photo"]["sourceKey"] }) | Select-Object -First 1
        Assert-Condition ($null -ne $match) "source deletion enters Removed from source after synchronization"
        Assert-Condition ([string]$match.revisionId -eq [string]$state["photo"]["revisionId"]) "removed entry retains the previously verified revision for review"
    }

    "VerifyPhotoExclusion" {
        $state = Get-State
        Assert-Condition ($null -ne $state["photo"]) "photo baseline is available"
        $exclusions = @(Get-Exclusions)
        $match = Get-MatchingExclusion -Exclusions $exclusions -SourceId "" -SourceKey ([string]$state["photo"]["sourceKey"])
        Assert-Condition ($null -ne $match) "still-present photo has a durable source-copy exclusion"

        $previewStatus = Get-HttpStatus "/api/collections/photos/$([Uri]::EscapeDataString([string]$state["photo"]["revisionId"]))/viewer-preview"
        Assert-Condition ($previewStatus -lt 200 -or $previewStatus -ge 300) "still-present excluded photo is immediately blocked from viewer-preview access"

        $state["photo"]["sourceId"] = [string]$match.sourceId
        $state["photo"]["purgeState"] = [string]$match.purgeState
        Save-State $state
        Write-Host "Current purge state for the photo: $($match.purgeState)"
    }

    "WaitForPurge" {
        $state = Get-State
        $targets = @(Get-TargetLocators -State $state -RequestedTarget $Target)
        Assert-Condition ($targets.Count -gt 0) "at least one recorded target is available for purge monitoring"

        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
        $allCompleted = $false
        do {
            $exclusions = @(Get-Exclusions)
            $states = @()
            $allCompleted = $true
            foreach ($targetItem in $targets) {
                $match = Get-MatchingExclusion -Exclusions $exclusions -SourceId ([string]$targetItem.sourceId) -SourceKey ([string]$targetItem.sourceKey)
                if ($null -eq $match) {
                    throw "VERIFY FAILED: a recorded target no longer has an exclusion tombstone."
                }

                $states += [string]$match.purgeState
                if ($match.purgeState -eq "failed") {
                    throw "VERIFY FAILED: privacy purge entered failed state with code '$($match.purgeErrorCode)'."
                }
                if ($match.purgeState -ne "completed") {
                    $allCompleted = $false
                }
            }

            $summary = $states | Group-Object | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }
            Write-Host "Purge state: $($summary -join ', ')"
            if ($allCompleted) {
                Write-Pass "all selected privacy purges completed"
                break
            }

            Start-Sleep -Seconds 2
        } while ([DateTimeOffset]::UtcNow -lt $deadline)

        if (-not $allCompleted) {
            throw "VERIFY FAILED: purge did not complete within $TimeoutSeconds seconds."
        }
    }

    "VerifyRestore" {
        $state = Get-State
        Assert-Condition ($null -ne $state["photo"]) "photo baseline is available"
        Assert-Condition ($state["photo"].ContainsKey("sourceId")) "photo exclusion locator was recorded; run VerifyPhotoExclusion before restore verification"

        $exclusions = @(Get-Exclusions)
        $match = Get-MatchingExclusion -Exclusions $exclusions -SourceId ([string]$state["photo"]["sourceId"]) -SourceKey ([string]$state["photo"]["sourceKey"])
        Assert-Condition ($null -eq $match) "re-include removed the completed exclusion tombstone"

        $items = @(Get-ArchiveItems -Analysis "all")
        $restored = @($items | Where-Object { $_.relativePath -eq [string]$state["photo"]["sourceKey"] -and -not [string]::IsNullOrWhiteSpace([string]$_.revisionId) }) | Select-Object -First 1
        Assert-Condition ($null -ne $restored) "source copy was re-catalogued after re-include"
        Assert-Condition ([string]$restored.revisionId -ne [string]$state["photo"]["revisionId"]) "re-include produced fresh catalogue processing rather than restoring the purged revision"
    }

    "VerifyExcludedRename" {
        $state = Get-State
        Assert-Condition ($null -ne $state["photo"]) "photo baseline is available"
        Assert-Condition ($state["photo"].ContainsKey("sourceId")) "old exclusion locator is recorded"
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($NewSourceKey)) "new relative source key was supplied"

        $exclusions = @(Get-Exclusions)
        $old = Get-MatchingExclusion -Exclusions $exclusions -SourceId ([string]$state["photo"]["sourceId"]) -SourceKey ([string]$state["photo"]["sourceKey"])
        Assert-Condition ($null -ne $old) "old source locator remains excluded after rename"
        $newExclusion = Get-MatchingExclusion -Exclusions $exclusions -SourceId "" -SourceKey $NewSourceKey
        Assert-Condition ($null -eq $newExclusion) "new source locator is independently included"

        $items = @(Get-ArchiveItems -Analysis "all")
        $newItem = @($items | Where-Object { $_.relativePath -eq $NewSourceKey -and -not [string]::IsNullOrWhiteSpace([string]$_.revisionId) }) | Select-Object -First 1
        Assert-Condition ($null -ne $newItem) "renamed source appears as a new included catalogue copy"
        Assert-Condition ([string]$newItem.revisionId -ne [string]$state["photo"]["revisionId"]) "renamed excluded source is catalogued as fresh work"
        Show-PrivateValue $NewSourceKey
    }
}
