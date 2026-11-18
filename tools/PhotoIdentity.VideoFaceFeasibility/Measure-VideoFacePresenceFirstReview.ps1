<#
.SYNOPSIS
Read-only aggregate validation of a private WI-0155 presence-first crop CSV.
No SQL, source video or frames, models, mutable group assignments, or
persistent identity writes. Unlike old group metrics, unreviewed groups are
legitimate; crop-level person proposals are deduplicated.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReviewedCsvPath,
    [Parameter(Mandatory)][string]$PrivateGroupReportPath,
    [Parameter(Mandatory)][string]$PrivatePeopleMapPath,
    [Parameter(Mandatory)][ValidatePattern('^V(?!00)[0-9]{2}$')][string]$ClipAlias
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$stage='private-input'
function LocalFile([string]$Path,[long]$Limit) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw 'Expected absolute private file path.'
    }
    $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $blocked=[long][IO.FileAttributes]::ReparsePoint -bor
        [long][IO.FileAttributes]::Offline -bor [long]0x00040000 -bor [long]0x00400000
    if ($item.PSIsContainer -or $item.Length -lt 1 -or $item.Length -gt $Limit -or
        ([long]$item.Attributes -band $blocked) -ne 0 -or
        (([long](Get-Item -LiteralPath $item.DirectoryName -Force).Attributes -band $blocked) -ne 0)) {
        throw 'Private file is unavailable, linked, online-only or oversized.'
    }
    return $item.FullName
}
function Ref([string]$Group,[int]$Frame,[int]$Face) {
    return ('{0}:{1}:{2}' -f $Group,$Frame,$Face)
}
try {
    $csv=LocalFile $ReviewedCsvPath (256KB)
    $source=LocalFile $PrivateGroupReportPath (64MB)
    $peopleFile=LocalFile $PrivatePeopleMapPath (4MB)
    $report=Get-Content -LiteralPath $source -Raw -Encoding utf8 | ConvertFrom-Json
    $map=Get-Content -LiteralPath $peopleFile -Raw -Encoding utf8 | ConvertFrom-Json
    $hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $clips=@($report.clips | Where-Object alias -CEQ $ClipAlias)
    if ([int]$report.schemaVersion -ne 1 -or $clips.Count -ne 1 -or
        [int]$map.schemaVersion -ne 1 -or
        [string]$map.pgSnapshot -cne [string]$report.catalogue.pgSnapshot -or
        [int]$report.catalogue.selectedPeople -ne @($map.people).Count -or
        [int]$report.catalogue.eligiblePeople -lt 1 -or
        [int]$report.catalogue.eligiblePeople -gt @($map.people).Count) {
        throw 'Catalogue and private alias map do not belong to one snapshot.'
    }
    $eligible=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($person in @($map.people)) {
        $id=[Guid]::Empty
        if ([string]$person.alias -cnotmatch '^P(?!0000)[0-9]{4}$' -or
            -not $eligible.Add([string]$person.alias) -or
            -not [Guid]::TryParse([string]$person.personId,[ref]$id) -or
            $id -eq [Guid]::Empty) {
            throw 'Invalid private people alias map.'
        }
    }
    $sourceGroups=$clips[0].review.diagnostics.provisionalGroups
    $groups=@($sourceGroups.groups)
    if ([int]$sourceGroups.schemaVersion -ne 1 -or
        [int]$sourceGroups.maximumGroups -ne 64 -or
        $groups.Count -lt 1 -or $groups.Count -gt 64 -or
        [int]$sourceGroups.observationsConsidered -ne
          [int]$clips[0].review.aggregate.facesDetected -or
        [int]$sourceGroups.observationsAssigned +
        [int]$sourceGroups.observationsUnassigned -ne
          [int]$sourceGroups.observationsConsidered) {
        throw 'Missing or inconsistent original provisional grouping.'
    }
    $stage='crop-source-binding'
    $expected=[Collections.Generic.Dictionary[string,object]]::new(
        [StringComparer]::Ordinal)
    $groupCounts=[Collections.Generic.Dictionary[string,int]]::new(
        [StringComparer]::Ordinal)
    $seenObservations=[Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $totalAssigned=0
    foreach($group in $groups) {
        $groupId=[string]$group.alias
        $members=[int]$group.observations
        $crops=@($group.representativeCrops)
        if ($groupId -cnotmatch '^G(?!000)[0-9]{3}$' -or
            $groupCounts.ContainsKey($groupId) -or $members -lt 1 -or
            $crops.Count -lt 1 -or $crops.Count -gt 5 -or
            $crops.Count -gt $members) {
            throw 'Invalid provisional group crop evidence.'
        }
        $groupCounts.Add($groupId,0)
        $totalAssigned+=$members
        foreach($crop in $crops) {
            $frame=[int]$crop.frameNumber
            $face=[int]$crop.faceNumber
            $key=Ref $groupId $frame $face
            $sourceObservation=('{0}:{1}' -f $frame,$face)
            if ($frame -lt 1 -or $frame -gt 180 -or
                $face -lt 1 -or $face -gt 32 -or
                -not $expected.TryAdd($key,$crop) -or
                -not $seenObservations.Add($sourceObservation)) {
                throw 'Duplicate or out-of-bounds representative crop.'
            }
            $groupCounts[$groupId]++
        }
    }
    if ($totalAssigned -ne [int]$sourceGroups.observationsAssigned) {
        throw 'Provisional group observations do not reconcile.'
    }

    $stage='review-csv'
    $rows=@(Import-Csv -LiteralPath $csv -Encoding utf8)
    $columns=@('Clip','ReportSha256','Group','Frame','Face','Decision',
        'PersonAlias','ExcludeFromGroup','ReviewerNotes')
    if ($rows.Count -ne $expected.Count -or
        $rows.Count -lt 1 -or $rows.Count -gt 320 -or
        (@($rows[0].PSObject.Properties.Name) -join '|') -cne ($columns -join '|')) {
        throw 'Presence-first worksheet columns or row count differ from source.'
    }
    $rowKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $known=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $distinctGroupPeople=@{}
    $reviewedGroups=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $confirmed=0;$unknown=0;$background=0;$uncertain=0
    $unreviewed=0;$excluded=0
    foreach($row in $rows) {
        $frame=0;$face=0
        if ([string]$row.Clip -cne $ClipAlias -or
            [string]$row.ReportSha256 -cne $hash -or
            [string]$row.Group -cnotmatch '^G(?!000)[0-9]{3}$' -or
            [string]$row.Frame -cnotmatch '^[1-9][0-9]{0,2}$' -or
            [string]$row.Face -cnotmatch '^[1-9][0-9]{0,1}$' -or
            -not [int]::TryParse([string]$row.Frame,[ref]$frame) -or
            -not [int]::TryParse([string]$row.Face,[ref]$face) -or
            $frame -gt 180 -or $face -gt 32 -or
            [string]$row.Decision -cnotin @('','Confirm person','Real face unknown','Not a face','Cannot tell') -or
            [string]$row.ExcludeFromGroup -cnotin @('True','False') -or
            [string]$row.ReviewerNotes.Length -gt 1000) {
            throw 'Invalid crop-level decision or source key.'
        }
        $key=Ref ([string]$row.Group) $frame $face
        if (-not $expected.ContainsKey($key) -or -not $rowKeys.Add($key)) {
            throw 'Review row does not match unique saved crop.'
        }
        $decision=[string]$row.Decision
        $person=[string]$row.PersonAlias
        if ($decision -ceq 'Confirm person') {
            if (-not $eligible.Contains($person)) {
                throw 'Confirmed person alias not present in private catalogue snapshot.'
            }
            [void]$known.Add($person)
            if (-not $distinctGroupPeople.ContainsKey([string]$row.Group)) {
                $distinctGroupPeople[[string]$row.Group]=
                    [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            }
            [void]$distinctGroupPeople[[string]$row.Group].Add($person)
            $confirmed++
        } elseif ($person -cne '') {
            throw 'A non-confirming crop may not carry a person identity.'
        } else {
            switch ($decision) {
                '' { $unreviewed++ }
                'Real face unknown' { $unknown++ }
                'Not a face' { $background++ }
                'Cannot tell' { $uncertain++ }
            }
        }
        if ($decision -cne '') { [void]$reviewedGroups.Add([string]$row.Group) }
        if ([string]$row.ExcludeFromGroup -ceq 'True') {
            if ($decision -ceq '') {
                throw 'Exclusion requires an explicit human-reviewed crop decision.'
            }
            $excluded++
        }
    }
    $mixed=@($distinctGroupPeople.Values | Where-Object { $_.Count -gt 1 }).Count
    [pscustomobject][ordered]@{
        SchemaVersion=1;Mode='PresenceFirst';InputEvidenceValidated=$true
        TotalProvisionalGroups=$groups.Count
        RepresentativeCrops=$expected.Count
        ConfirmedRepresentativeCrops=$confirmed
        KnownPersonPresenceProposals=$known.Count
        DuplicateKnownPersonConfirmations=$confirmed-$known.Count
        UnknownFaceRepresentativeCrops=$unknown
        BackgroundRepresentativeCrops=$background
        CannotTellRepresentativeCrops=$uncertain
        UnreviewedRepresentativeCrops=$unreviewed
        GroupsWithoutReviewedCrops=$groups.Count-$reviewedGroups.Count
        MixedIdentityGroups=$mixed
        ExcludedCropAnnotations=$excluded
        UnshownGroupObservations=$totalAssigned-$expected.Count
        UnassignedGroupObservations=[int]$sourceGroups.observationsUnassigned
        PersonCoverageProven=$false
        ProductionAssignmentsMade=0
    }
} catch {
    # The caller may report only the safe stage: never emit private aliases,
    # actual paths, timestamps, crops, notes or individual rankings.
    throw ('WI-0155 private presence-first audit rejected inconsistent evidence (stage: '+$stage+').')
}
