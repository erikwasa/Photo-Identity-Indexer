<#
.SYNOPSIS
Read-only, private WI-0155 group-rank accuracy and candidate-triage audit.
.DESCRIPTION
Joins a reviewed group CSV with the *matching* full-catalogue report and
private alias map. Outputs aggregate numbers only. Never emits a person ID,
name, alias, group ID, score for an individual group, path, or note.
No inference, SQL, original-media access or persistent output.
Human labels in SelectedPerson are P0001 aliases from the PRIVATE people map,
or UNKNOWN for a genuine face not present in the eligible catalogue.
An empty label is UNREVIEWED, not a negative identification.
.EXAMPLE
.\Measure-VideoFaceGroupRankAccuracy.ps1 -ReviewedCsvPath $csv -PrivateGroupReportPath $report -PrivatePeopleMapPath $map -ClipAlias V03
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReviewedCsvPath,
    [Parameter(Mandatory)][string]$PrivateGroupReportPath,
    [Parameter(Mandatory)][string]$PrivatePeopleMapPath,
    [Parameter(Mandatory)][ValidatePattern('^V(?!00)[0-9]{2}$')][string]$ClipAlias
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-PrivateFile([string]$Path, [long]$MaximumBytes) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw 'Private file must have a full local path.'
    }
    $file = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $blocked = [long][IO.FileAttributes]::ReparsePoint -bor
        [long][IO.FileAttributes]::Offline -bor
        [long]0x00040000 -bor [long]0x00400000
    if ($file.PSIsContainer -or $file.Length -lt 1 -or
        $file.Length -gt $MaximumBytes -or
        ([long]$file.Attributes -band $blocked) -ne 0 -or
        ((Get-Item -LiteralPath $file.DirectoryName).Attributes -band
            [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Private file is unavailable, linked, online-only or oversized.'
    }
    return $file.FullName
}

function Median([double[]]$Values) {
    if ($null -eq $Values -or $Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    $n = $sorted.Count
    if ($n % 2 -eq 1) { return [Math]::Round([double]$sorted[[int]($n / 2 - 0.5)],4) }
    return [Math]::Round(
        ([double]$sorted[($n / 2 - 1)] + [double]$sorted[($n / 2)]) / 2, 4)
}

try {
    $csv = Assert-PrivateFile $ReviewedCsvPath (256KB)
    $json = Assert-PrivateFile $PrivateGroupReportPath (64MB)
    $mapFile = Assert-PrivateFile $PrivatePeopleMapPath (1MB)

    # Reuse the established CSV decision/merge-cycle/count validator.
    $baseline = & (Join-Path $PSScriptRoot 'Measure-VideoFaceGroupReview.ps1') -ReviewedCsvPath $csv -PrivateGroupReportPath $json -ClipAlias $ClipAlias
    $rows = @(Import-Csv -LiteralPath $csv -Encoding utf8)
    $report = Get-Content -LiteralPath $json -Raw -Encoding utf8 | ConvertFrom-Json
    $privateMap = Get-Content -LiteralPath $mapFile -Raw -Encoding utf8 | ConvertFrom-Json

    $clips = @($report.clips | Where-Object { $_.alias -ceq $ClipAlias })
    if ([int]$report.schemaVersion -ne 1 -or $clips.Count -ne 1 -or
        [int]$privateMap.schemaVersion -ne 1 -or
        [string]$report.catalogue.pgSnapshot -cne [string]$privateMap.pgSnapshot -or
        [int]$report.catalogue.selectedPeople -ne @($privateMap.people).Count -or
        [int]$report.catalogue.eligiblePeople -lt 1 -or
        [int]$report.catalogue.eligiblePeople -gt [int]$report.catalogue.selectedPeople) {
        throw 'Private catalogue evidence and map are inconsistent.'
    }

    $people = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($person in @($privateMap.people)) {
        $alias = [string]$person.alias
        $id = [Guid]::Empty
        if ($alias -cnotmatch '^P(?!0000)[0-9]{4}$' -or
            -not $people.Add($alias) -or
            -not [Guid]::TryParse([string]$person.personId, [ref]$id) -or
            $id -eq [Guid]::Empty -or -not $ids.Add($id.ToString('D'))) {
            throw 'Private people-map entries are inconsistent.'
        }
    }

    $clip = $clips[0]
    $groupReview = $clip.review.diagnostics.provisionalGroups
    $groups = @($groupReview.groups)
    $ranked = @($clip.review.diagnostics.groupSuggestions)
    if ($groups.Count -ne $ranked.Count -or $ranked.Count -ne $rows.Count) {
        throw 'The report does not contain complete group rankings.'
    }

    $byGroup = [Collections.Generic.Dictionary[string,object]]::new(
        [StringComparer]::Ordinal)
    foreach ($g in $groups) {
        if (-not $byGroup.TryAdd([string]$g.alias, $g)) {
            throw 'Duplicate report group.'
        }
    }
    $byRank = [Collections.Generic.Dictionary[string,object]]::new(
        [StringComparer]::Ordinal)
    foreach ($rank in $ranked) {
        $alias = [string]$rank.groupAlias
        if (-not $byRank.TryAdd($alias, $rank) -or
            -not $byGroup.ContainsKey($alias) -or
            [int]$rank.groupObservations -ne [int]$byGroup[$alias].observations -or
            [int]$rank.representativeCount -ne @($byGroup[$alias].representativeCrops).Count) {
            throw 'Group suggestions are not from the reviewed group evidence.'
        }
        $candidates = @($rank.topCandidates)
        if ($candidates.Count -ne [Math]::Min(3, [int]$report.catalogue.eligiblePeople)) {
            throw 'Incomplete person ranks.'
        }
        $distinct = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $previous = [double]::PositiveInfinity
        foreach ($candidate in $candidates) {
            $score = [double]$candidate.score
            if (-not $people.Contains([string]$candidate.alias) -or
                -not $distinct.Add([string]$candidate.alias) -or
                -not [double]::IsFinite($score) -or
                $score -lt -1 -or $score -gt 1 -or $score -gt $previous) {
                throw 'Invalid or unordered top-three person candidates.'
            }
            $previous = $score
        }
    }

    $known = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $knownTop1 = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $knownTop3 = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $dismissedConf = [Collections.Generic.List[double]]::new()
    $retainedConf = [Collections.Generic.List[double]]::new()
    $dismissedQuality = [Collections.Generic.List[double]]::new()
    $retainedQuality = [Collections.Generic.List[double]]::new()
    $dismissedScore = [Collections.Generic.List[double]]::new()
    $retainedScore = [Collections.Generic.List[double]]::new()
    $knownGroups = 0
    $unknownGroups = 0
    $unlabelledRetained = 0
    $top1 = 0
    $top3 = 0
    $knownNotTop3 = 0
    $dismissedExploratory = 0
    $dismissedExploratoryWithMargin = 0
    $retainedExploratory = 0

    foreach ($row in $rows) {
        $groupAlias = [string]$row.Group
        $rank = $byRank[$groupAlias]
        $person = [string]$row.SelectedPerson
        $decision = [string]$row.Decision
        if ($person -ne '' -and $person -cne 'UNKNOWN' -and
            -not $people.Contains($person)) {
            throw 'Human selected-person alias is not in this snapshot.'
        }
        if ($person -ne '' -and
            $decision -notin @('Keep group','Merge group')) {
            throw 'Only reviewed retained real-face groups can have a person label.'
        }
        $candidate = @($rank.topCandidates)
        $score = [double]$candidate[0].score
        $gap = if ($candidate.Count -gt 1) {
            $score - [double]$candidate[1].score
        } else { $null }
        $exploratory = $score -ge 0.80
        $strong = $exploratory -and $null -ne $gap -and $gap -ge 0.05
        $samples = @($byGroup[$groupAlias].representativeCrops)
        if ($samples.Count -lt 1 -or $samples.Count -gt 5) {
            throw 'Group lacks the representative crop diagnostic.'
        }
        $confValues = [Collections.Generic.List[double]]::new()
        $qualityValues = [Collections.Generic.List[double]]::new()
        foreach ($sample in $samples) {
            $confidence = [double]$sample.detectorConfidence
            $quality = [double]$sample.quality
            if (-not [double]::IsFinite($confidence) -or
                -not [double]::IsFinite($quality) -or
                $confidence -lt 0 -or $confidence -gt 1 -or
                $quality -lt 0 -or $quality -gt 1) {
                throw 'Invalid representative-crop quality diagnostic.'
            }
            $confValues.Add($confidence)
            $qualityValues.Add($quality)
        }
        $meanConfidence = ($confValues | Measure-Object -Average).Average
        $meanQuality = ($qualityValues | Measure-Object -Average).Average

        if ($decision -ceq 'Dismiss false detection') {
            $dismissedScore.Add($score)
            $dismissedConf.Add([double]$meanConfidence)
            $dismissedQuality.Add([double]$meanQuality)
            if ($exploratory) { $dismissedExploratory++ }
            if ($strong) { $dismissedExploratoryWithMargin++ }
        }
        if ($decision -in @('Keep group','Merge group')) {
            $retainedScore.Add($score)
            $retainedConf.Add([double]$meanConfidence)
            $retainedQuality.Add([double]$meanQuality)
            if ($exploratory) { $retainedExploratory++ }
            if ($person -ceq '') {
                $unlabelledRetained++
            } elseif ($person -ceq 'UNKNOWN') {
                $unknownGroups++
            } else {
                $knownGroups++
                [void]$known.Add($person)
                if ([string]$candidate[0].alias -ceq $person) {
                    $top1++
                    [void]$knownTop1.Add($person)
                }
                if (@($candidate | Where-Object { $_.alias -ceq $person }).Count -gt 0) {
                    $top3++
                    [void]$knownTop3.Add($person)
                } else {
                    $knownNotTop3++
                }
            }
        }
    }

    # A missing label is NOT a wrong suggestion. Distinct-person counts
    # intentionally do not include unreviewed, unknown or unassigned faces.
    [pscustomobject][ordered]@{
        SchemaVersion = 1
        InputEvidenceValidated = $true
        CanonicalPeople = [int]$report.catalogue.selectedPeople
        EligiblePeople = [int]$report.catalogue.eligiblePeople
        ExemplarsLoaded = [int]$report.catalogue.exemplarsLoaded
        TotalDetectedCandidates = [int]$clip.review.aggregate.facesDetected
        AssignedObservations = [int]$baseline.AssignedGroupObservations
        UnassignedObservations = [int]$baseline.ObservationsUnassigned
        GroupCapacityReached = [bool]$baseline.GroupCapacityReached
        TotalGroups = [int]$baseline.TotalGroups
        DismissedGroups = [int]$baseline.DismissedGroups
        RetainedGroups = [int]$baseline.KeptGroups + [int]$baseline.MergeGroups
        LabelledKnownGroups = $knownGroups
        LabelledUnknownGroups = $unknownGroups
        UnlabelledRetainedGroups = $unlabelledRetained
        CorrectTop1Groups = $(if ($knownGroups -gt 0) { $top1 } else { $null })
        CorrectTop3Groups = $(if ($knownGroups -gt 0) { $top3 } else { $null })
        MissedTop3Groups = $(if ($knownGroups -gt 0) { $knownNotTop3 } else { $null })
        DistinctKnownPeopleLabelled = $(if ($knownGroups -gt 0) { $known.Count } else { $null })
        DistinctKnownPeopleTop1 = $(if ($knownGroups -gt 0) { $knownTop1.Count } else { $null })
        DistinctKnownPeopleTop3 = $(if ($knownGroups -gt 0) { $knownTop3.Count } else { $null })
        DistinctKnownPeopleMissedTop3 = $(if ($knownGroups -gt 0) { $known.Count - $knownTop3.Count } else { $null })
        DismissedGroupsAboveExploratorySimilarity = $dismissedExploratory
        DismissedGroupsWithExploratoryReviewLead = $dismissedExploratoryWithMargin
        RetainedGroupsAboveExploratorySimilarity = $retainedExploratory
        MedianDismissedGroupDetectorConfidence = Median $dismissedConf.ToArray()
        MedianRetainedGroupDetectorConfidence = Median $retainedConf.ToArray()
        MedianDismissedGroupQualityProxy = Median $dismissedQuality.ToArray()
        MedianRetainedGroupQualityProxy = Median $retainedQuality.ToArray()
        MedianDismissedGroupTopCatalogueScore = Median $dismissedScore.ToArray()
        MedianRetainedGroupTopCatalogueScore = Median $retainedScore.ToArray()
    }
}
catch {
    # No raw exception message may leak a private path, alias, ID or score.
    throw 'WI-0155 private group rank audit rejected unavailable or inconsistent evidence.'
}
