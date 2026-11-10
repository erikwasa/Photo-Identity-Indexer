<#
.SYNOPSIS
Read-only, private, aggregate measurement of WI-0155 provisional group reviews.
.DESCRIPTION
Reads an operator-saved offline group CSV and optionally the matching private
--provisional-groups catalogue JSON. Emits aggregate counts ONLY. No images,
embeddings, canonical person IDs, group aliases, notes or file paths are emitted.
Does not connect to PostgreSQL or modify inputs.
.EXAMPLE
.\Measure-VideoFaceGroupReview.ps1 -ReviewedCsvPath $reviewCsv -PrivateGroupReportPath $newGroupReport
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReviewedCsvPath,
    [string]$PrivateGroupReportPath,
    [ValidatePattern('^V(?!00)[0-9]{2}$')][string]$ClipAlias
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-LocalRegularFile {
    param([string]$Path, [long]$MaxLength)
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw 'Expected a fully qualified private file.'
    }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $blocked = [long][IO.FileAttributes]::ReparsePoint -bor
        [long][IO.FileAttributes]::Offline -bor [long]0x00040000 -bor
        [long]0x00400000
    if ($item.PSIsContainer -or $item.Length -lt 1 -or
        $item.Length -gt $MaxLength -or
        ([long]$item.Attributes -band $blocked) -ne 0 -or
        ((Get-Item -LiteralPath $item.DirectoryName).Attributes -band
            [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Expected a bounded fully local private file.'
    }
    return $item.FullName
}

try {
    $csv = Test-LocalRegularFile $ReviewedCsvPath (256KB)
    $rows = @(Import-Csv -LiteralPath $csv -Encoding utf8)
    if ($rows.Count -lt 1 -or $rows.Count -gt 64) {
        throw 'Review must contain 1 to 64 provisional groups.'
    }

    $columns = @('Clip','Group','Observations','Decision','MergeInto',
        'SelectedPerson','ReviewerNotes')
    if ((@($rows[0].PSObject.Properties.Name) -join '|') -cne
        ($columns -join '|')) {
        throw 'Unexpected review worksheet columns.'
    }

    $videoAliases = @($rows | ForEach-Object { [string]$_.Clip } |
        Sort-Object -Unique)
    if ($videoAliases.Count -ne 1 -or
        $videoAliases[0] -cnotmatch '^V(?!00)[0-9]{2}$' -or
        ($ClipAlias -and $videoAliases[0] -cne $ClipAlias)) {
        throw 'Expected a single approved video alias.'
    }

    $allowedDecisions = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($label in @('','Keep group','Merge group',
            'Dismiss false detection','Split required','Cannot tell')) {
        [void]$allowedDecisions.Add($label)
    }
    $byAlias = [Collections.Generic.Dictionary[string,object]]::new(
        [StringComparer]::Ordinal)
    $observations = 0
    foreach ($row in $rows) {
        if ([string]$row.Group -cnotmatch '^G(?!000)[0-9]{3}$' -or
            -not $byAlias.TryAdd([string]$row.Group, $row) -or
            -not $allowedDecisions.Contains([string]$row.Decision) -or
            [string]$row.Observations -cnotmatch '^[1-9][0-9]{0,6}$') {
            throw 'Invalid or duplicate group evidence.'
        }
        $observations += [int]$row.Observations
    }

    foreach ($row in $rows) {
        if ([string]$row.Decision -ne 'Merge group') { continue }
        $visited = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $current = $row
        while ([string]$current.Decision -eq 'Merge group') {
            if (-not $visited.Add([string]$current.Group) -or
                -not $byAlias.ContainsKey([string]$current.MergeInto) -or
                [string]$current.MergeInto -ceq [string]$current.Group) {
                throw 'Invalid or circular group merge.'
            }
            $current = $byAlias[[string]$current.MergeInto]
        }
        if ([string]$current.Decision -ne 'Keep group') {
            throw 'A merge must terminate at a kept group.'
        }
    }

    $kept = @($rows | Where-Object Decision -CEQ 'Keep group')
    $merged = @($rows | Where-Object Decision -CEQ 'Merge group')
    $dismissed = @($rows | Where-Object Decision -CEQ 'Dismiss false detection')
    $uncertain = @($rows | Where-Object Decision -CEQ 'Cannot tell')
    $split = @($rows | Where-Object Decision -CEQ 'Split required')
    $pending = @($rows | Where-Object Decision -CEQ '')
    $active = @($rows | Where-Object {
        $_.Decision -in @('Keep group','Merge group')
    })
    $dismissedObservations = [int](($dismissed |
        Measure-Object -Property Observations -Sum).Sum)
    $activeObservations = [int](($active |
        Measure-Object -Property Observations -Sum).Sum)
    $unresolved = $uncertain.Count + $split.Count + $pending.Count

    $unassigned = $null
    $atCapacity = $null
    if (-not [string]::IsNullOrWhiteSpace($PrivateGroupReportPath)) {
        $jsonPath = Test-LocalRegularFile $PrivateGroupReportPath (64MB)
        $report = Get-Content -LiteralPath $jsonPath -Raw -Encoding utf8 |
            ConvertFrom-Json
        $clips = @($report.clips | Where-Object {
            $_.alias -ceq $videoAliases[0]
        })
        if ([int]$report.schemaVersion -ne 1 -or
            $clips.Count -ne 1 -or
            $null -eq $clips[0].review.diagnostics.provisionalGroups) {
            throw 'Expected corresponding private grouped report.'
        }
        $review = $clips[0].review.diagnostics.provisionalGroups
        $reportGroups = @($review.groups)
        if ([int]$review.schemaVersion -ne 1 -or
            [int]$review.maximumGroups -ne 64 -or
            $reportGroups.Count -ne $rows.Count -or
            [int]$review.observationsAssigned -ne $observations -or
            [int]$review.observationsConsidered -ne
                ([int]$review.observationsAssigned +
                 [int]$review.observationsUnassigned) -or
            [int]$review.observationsConsidered -ne
                [int]$clips[0].review.aggregate.facesDetected) {
            throw 'Group report totals do not match the reviewed CSV.'
        }
        $expected = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($group in $reportGroups) {
            $id = [string]$group.alias
            if (-not $expected.Add($id) -or
                -not $byAlias.ContainsKey($id) -or
                [int]$byAlias[$id].Observations -ne
                    [int]$group.observations) {
                throw 'Group report and CSV identities or counts differ.'
            }
        }
        $unassigned = [int]$review.observationsUnassigned
        $atCapacity = $reportGroups.Count -eq [int]$review.maximumGroups
    }

    # Output deliberately excludes group aliases, notes, identities,
    # timestamp positions, private paths and any source image data.
    [pscustomobject][ordered]@{
        SchemaVersion = 1
        GroupsReviewed = $rows.Count - $pending.Count
        TotalGroups = $rows.Count
        KeptGroups = $kept.Count
        MergeGroups = $merged.Count
        DismissedGroups = $dismissed.Count
        SplitRequiredGroups = $split.Count
        CannotTellGroups = $uncertain.Count
        UnreviewedGroups = $pending.Count
        ProposedFinalGroups = $(if ($unresolved -eq 0) { $kept.Count } else { $null })
        AssignedGroupObservations = $observations
        KeptOrMergeObservations = $activeObservations
        DismissedGroupObservations = $dismissedObservations
        ObservationsUnassigned = $unassigned
        GroupCapacityReached = $atCapacity
        DismissedGroupPercentage = [Math]::Round(
            100.0 * $dismissed.Count / $rows.Count, 1)
        DismissedAssignedObservationPercentage = [Math]::Round(
            100.0 * $dismissedObservations / $observations, 1)
        PrivateInputValidated = $true
    }
}
catch {
    # Do not disclose private file paths, clip aliases, group IDs or
    # review annotations in terminal output/CI logs.
    throw 'WI-0155 private group measurement rejected inconsistent or unsafe inputs.'
}
