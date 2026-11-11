<#
.SYNOPSIS
Private, read-only counterfactual review ordering for WI-0155.
.DESCRIPTION
Compares deterministic review-priority policies using existing operator-
labelled groups and their immutable private V03 report. No rejection, model
inference, database access, video reads, writes or person assignment.
An output row is an AGGREGATE about reviewed groups only. It cannot measure
the 3,505 unassigned observations or true face recall for the full clip.
Policies are evaluation hypotheses, NOT recommended calibrated filters.
.EXAMPLE
.\Compare-VideoFaceReviewPriority.ps1 -ReviewedCsvPath $reviewCsv -PrivateGroupReportPath $report -PrivatePeopleMapPath $map -ClipAlias V03
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReviewedCsvPath,
    [Parameter(Mandatory)][string]$PrivateGroupReportPath,
    [Parameter(Mandatory)][string]$PrivatePeopleMapPath,
    [Parameter(Mandatory)][ValidatePattern('^V(?!00)[0-9]{2}$')][string]$ClipAlias,
    [ValidateRange(1,64)][int[]]$ReviewBudgets = @(8,16,32,48,64)
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function LocalPrivateFile([string]$Path,[long]$MaximumBytes) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw 'A fully qualified private file is required.'
    }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $blocked = [long][IO.FileAttributes]::ReparsePoint -bor
        [long][IO.FileAttributes]::Offline -bor
        [long]0x00040000 -bor [long]0x00400000
    if ($item.PSIsContainer -or $item.Length -lt 1 -or
        $item.Length -gt $MaximumBytes -or
        ([long]$item.Attributes -band $blocked) -ne 0 -or
        ((Get-Item -LiteralPath $item.DirectoryName).Attributes -band
            [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'A private input is linked, offline, missing or oversized.'
    }
    return $item.FullName
}

function OrderedGroups([object[]]$Groups,[string]$Policy) {
    # Each score is independent of human labels; there is no target leakage.
    switch ($Policy) {
        'Arrival order' {
            return ,@($Groups | Sort-Object -Property @{Expression='Sequence';Ascending=$true})
        }
        'Detector confidence' {
            return ,@($Groups | Sort-Object -Property @{Expression='Confidence';Descending=$true}, @{Expression='Sequence';Ascending=$true})
        }
        'Crop quality proxy' {
            return ,@($Groups | Sort-Object -Property @{Expression='Quality';Descending=$true}, @{Expression='Sequence';Ascending=$true})
        }
        'Catalogue similarity' {
            return ,@($Groups | Sort-Object -Property @{Expression='CatalogueScore';Descending=$true}, @{Expression='Sequence';Ascending=$true})
        }
        'Confidence + quality' {
            return ,@($Groups | Sort-Object -Property @{Expression='Combined';Descending=$true}, @{Expression='Sequence';Ascending=$true})
        }
        'Time-spread + quality' {
            # Round-robin among four equally spaced temporal segments; no
            # distinct-person or ground-truth labels enter selection.
            $buckets = @(
                ,@($Groups | Where-Object Quarter -EQ 0 | Sort-Object -Property @{Expression='Combined';Descending=$true}, @{Expression='Sequence';Ascending=$true})
                ,@($Groups | Where-Object Quarter -EQ 1 | Sort-Object -Property @{Expression='Combined';Descending=$true}, @{Expression='Sequence';Ascending=$true})
                ,@($Groups | Where-Object Quarter -EQ 2 | Sort-Object -Property @{Expression='Combined';Descending=$true}, @{Expression='Sequence';Ascending=$true})
                ,@($Groups | Where-Object Quarter -EQ 3 | Sort-Object -Property @{Expression='Combined';Descending=$true}, @{Expression='Sequence';Ascending=$true})
            )
            $ordered = [Collections.Generic.List[object]]::new()
            $index = 0
            do {
                $added = $false
                foreach ($bucket in $buckets) {
                    if ($index -lt $bucket.Count) {
                        $ordered.Add($bucket[$index])
                        $added = $true
                    }
                }
                $index++
            } while ($added)
            return ,@($ordered.ToArray())
        }
        default { throw 'Unrecognized review priority policy.' }
    }
}

try {
    $csv = LocalPrivateFile $ReviewedCsvPath (256KB)
    $reportPath = LocalPrivateFile $PrivateGroupReportPath (64MB)
    $mapPath = LocalPrivateFile $PrivatePeopleMapPath (1MB)

    # This established validator verifies merge cycles, cross-file group counts,
    # snapshot provenance, correct alias mapping, rank uniqueness/ordering,
    # input-bounds, and complete assigned/unassigned accounting.
    $accepted = & (Join-Path $PSScriptRoot 'Measure-VideoFaceGroupRankAccuracy.ps1') -ReviewedCsvPath $csv -PrivateGroupReportPath $reportPath -PrivatePeopleMapPath $mapPath -ClipAlias $ClipAlias
    if (-not $accepted.InputEvidenceValidated -or
        $accepted.TotalGroups -lt 1 -or $accepted.TotalGroups -gt 64) {
        throw 'The source rank evidence was not verified.'
    }

    $report = Get-Content -LiteralPath $reportPath -Raw -Encoding utf8 |
        ConvertFrom-Json
    $rows = @(Import-Csv -LiteralPath $csv -Encoding utf8)
    $clip = @($report.clips | Where-Object { $_.alias -ceq $ClipAlias })[0]
    $groupRows = @($clip.review.diagnostics.provisionalGroups.groups)
    $rankRows = @($clip.review.diagnostics.groupSuggestions)
    $frames = [int]$clip.review.aggregate.framesExamined
    if ($frames -lt 1 -or $frames -gt 180 -or
        $rows.Count -ne $groupRows.Count -or
        $rows.Count -ne $rankRows.Count -or
        @($rows | Where-Object {
            $_.Decision -cnotin @('Keep group','Merge group','Dismiss false detection')
        }).Count -gt 0) {
        throw 'Counterfactual review requires a completely resolved group worksheet.'
    }

    $byGroup = @{}
    $byRank = @{}
    foreach ($group in $groupRows) { $byGroup[[string]$group.alias] = $group }
    foreach ($ranking in $rankRows) { $byRank[[string]$ranking.groupAlias] = $ranking }
    $data = [Collections.Generic.List[object]]::new()
    $allPeople = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $unlabelled = 0
    foreach ($row in $rows) {
        $group = $byGroup[[string]$row.Group]
        $rank = $byRank[[string]$row.Group]
        $samples = @($group.representativeCrops)
        $frameNumbers = @($samples | ForEach-Object { [int]$_.frameNumber })
        if ($frameNumbers.Count -lt 1 -or @($frameNumbers | Where-Object {
            $_ -lt 1 -or $_ -gt $frames
        }).Count -gt 0) {
            throw 'Group time provenance was invalid.'
        }
        $confidence = [double](($samples | Measure-Object -Property detectorConfidence -Average).Average)
        $quality = [double](($samples | Measure-Object -Property quality -Average).Average)
        $score = [double]@($rank.topCandidates)[0].score
        $person = [string]$row.SelectedPerson
        $isHumanFace = $row.Decision -cin @('Keep group','Merge group')
        if ($isHumanFace -and $person -eq '') { $unlabelled++ }
        if ($isHumanFace -and $person -ne '' -and $person -cne 'UNKNOWN') {
            [void]$allPeople.Add($person)
        }
        $timeFrame = [int](($frameNumbers | Measure-Object -Minimum).Minimum)
        $quarter = [Math]::Min(3, [int][Math]::Floor(
            4.0 * ($timeFrame - 1) / $frames))
        $candidateAliases = @($rank.topCandidates | ForEach-Object { [string]$_.alias })
        $data.Add([pscustomobject]@{
            Sequence = [int]([string]$row.Group).Substring(1)
            HumanFace = $isHumanFace
            Person = $person
            RankAliases = $candidateAliases
            Confidence = $confidence
            Quality = $quality
            CatalogueScore = $score
            Combined = ($confidence + $quality) / 2.0
            Quarter = $quarter
        })
    }

    $totalTrueFaces = @($data | Where-Object HumanFace -EQ $true).Count
    $totalBackground = $data.Count - $totalTrueFaces
    $hasCompleteLabels = $unlabelled -eq 0
    $policies = @(
        'Arrival order','Detector confidence','Crop quality proxy',
        'Catalogue similarity','Confidence + quality','Time-spread + quality'
    )
    $budgets = @(@($ReviewBudgets) + @($data.Count) |
        Sort-Object -Unique | Where-Object { $_ -le $data.Count })
    foreach ($policy in $policies) {
        $ordered = @(OrderedGroups $data.ToArray() $policy)
        if ($ordered.Count -ne $data.Count) {
            throw 'Candidate ordering failed to retain all review groups.'
        }
        foreach ($budget in $budgets) {
            $selected = @($ordered | Select-Object -First $budget)
            $trueShown = @($selected | Where-Object HumanFace -EQ $true)
            $backgroundShown = $selected.Count - $trueShown.Count
            $present = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal)
            $top3 = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal)
            foreach ($item in $trueShown) {
                if ($item.Person -eq '' -or $item.Person -ceq 'UNKNOWN') {
                    continue
                }
                [void]$present.Add($item.Person)
                if ($item.RankAliases -ccontains $item.Person) {
                    [void]$top3.Add($item.Person)
                }
            }
            # This is a counterfactual priority calculation across ONLY
            # the 64 reviewed groups; the 3505 unassigned detections have
            # no true-face labels and cannot enter any recall denominator.
            [pscustomobject][ordered]@{
                SchemaVersion = 1
                Policy = $policy
                ReviewBudgetGroups = [int]$budget
                OriginalReviewedGroups = [int]$data.Count
                ReviewedTrueFaceGroups = $totalTrueFaces
                ReviewedBackgroundGroups = $totalBackground
                TrueFaceGroupsShown = $trueShown.Count
                TrueFaceGroupsNotShown = $totalTrueFaces - $trueShown.Count
                BackgroundGroupsShown = $backgroundShown
                BackgroundGroupsAvoided = $totalBackground - $backgroundShown
                HumanPersonLabelsComplete = $hasCompleteLabels
                DistinctKnownPeopleInReviewedGroups = $(if ($hasCompleteLabels) { $allPeople.Count } else { $null })
                DistinctKnownPeopleShown = $(if ($hasCompleteLabels) { $present.Count } else { $null })
                DistinctKnownPeopleNotShown = $(if ($hasCompleteLabels) { $allPeople.Count - $present.Count } else { $null })
                DistinctKnownPeopleWithCorrectTop3Shown = $(if ($hasCompleteLabels) { $top3.Count } else { $null })
                ObservationsAssigned = [int]$accepted.AssignedObservations
                ObservationsUnassignedAndUnlabelled = [int]$accepted.UnassignedObservations
                OriginalGroupCapacityReached = [bool]$accepted.GroupCapacityReached
            }
        }
    }
}
catch {
    # Never print exception values containing private paths, aliases or scores.
    throw 'WI-0155 private review-priority comparison rejected inconsistent or unsafe evidence.'
}
