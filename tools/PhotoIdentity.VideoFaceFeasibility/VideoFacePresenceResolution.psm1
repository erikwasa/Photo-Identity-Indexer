<#
.SYNOPSIS
Pure, private WI-0155 human-reviewed video-person presence resolver.
.DESCRIPTION
Consumes already source-validated C/N/U rows from ONE combined review.
No SQL, video, models, reference enrollment, persistent writes or names.
All identity aliases are review evidence, never automatic assignments.
#>
Set-StrictMode -Version Latest

function Resolve-VideoFacePresenceEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object[]]$GroupRows,
        [Parameter(Mandatory)][object[]]$AuditRows,
        [Parameter(Mandatory)][hashtable]$RepresentativeFrames,
        [hashtable]$CompleteMemberFrames = @{}  # New private reports only
    )
    $knownPattern = '^P(?!0000$)[0-9]{4}$'
    $groupPattern = '^C(?!000)[0-9]{3}$'
    if ($GroupRows.Count -lt 1 -or $GroupRows.Count -gt 64 -or
        $AuditRows.Count -lt 2 -or $AuditRows.Count -gt 192) {
        throw 'Reviewed presence input exceeds source bounds.'
    }
    $byId = [Collections.Generic.Dictionary[string,object]]::new(
        [StringComparer]::Ordinal)
    $allowed = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($decision in @('','Keep group','Merge group',
        'Dismiss false detection','Split required','Cannot tell')) {
        [void]$allowed.Add($decision)
    }
    foreach ($row in $GroupRows) {
        $id = [string]$row.Group
        $decision = [string]$row.Decision
        $person = [string]$row.SelectedPerson
        if ($id -cnotmatch $groupPattern -or
            -not $byId.TryAdd($id,$row) -or
            -not $allowed.Contains($decision) -or
            ($person -cne '' -and $person -cne 'UNKNOWN' -and
             $person -cnotmatch $knownPattern) -or
            ($decision -cne 'Merge group' -and [string]$row.MergeInto -cne '') -or
            ($decision -notin @('Keep group','Merge group') -and $person -cne '') -or
            -not $RepresentativeFrames.ContainsKey($id)) {
            throw 'Invalid group, decision or reviewer identity evidence.'
        }
        foreach ($frame in @($RepresentativeFrames[$id])) {
            if ($frame -isnot [int] -or $frame -lt 1 -or $frame -gt 180) {
                throw 'Invalid representative frame evidence.'
            }
        }
    }
    if ($RepresentativeFrames.Count -ne $byId.Count) {
        throw 'Unexpected representative frame evidence.'
    }

    # Strictly validate every full-member list supplied by a new report.
    # A group has at most one distinct detection per source frame by the
    # original DBSCAN not-same-frame constraints. Every representative frame
    # MUST be present in its complete source membership.
    $verifiedFull = [Collections.Generic.Dictionary[string,int[]]]::new(
        [StringComparer]::Ordinal)
    foreach ($id in @($CompleteMemberFrames.Keys)) {
        if (-not $byId.ContainsKey([string]$id)) {
            throw 'Full-frame evidence contains an unknown C-group.'
        }
        $row = $byId[[string]$id]
        $sourceCount = 0
        if (-not [int]::TryParse([string]$row.Observations,[ref]$sourceCount) -or
            $sourceCount -lt 1 -or $sourceCount -gt 5760) {
            throw 'Full-frame evidence lacks an exact source group count.'
        }
        $values = @($CompleteMemberFrames[$id])
        if ($values.Count -ne $sourceCount) {
            throw 'Full-frame evidence does not cover all source observations.'
        }
        $numbered = [Collections.Generic.List[int]]::new()
        $previous = 0
        foreach ($v in $values) {
            $number = 0
            if ($v -isnot [int] -or
                -not [int]::TryParse([string]$v,[ref]$number) -or
                $number -le $previous -or $number -gt 180) {
                throw 'Full-frame membership is invalid, repeated or out of order.'
            }
            $numbered.Add($number)
            $previous = $number
        }
        $members = $numbered.ToArray()
        foreach ($f in @($RepresentativeFrames[$id])) {
            if ([array]::BinarySearch($members,[int]$f) -lt 0) {
                throw 'A representative crop is absent from complete frame membership.'
            }
        }
        $verifiedFull.Add([string]$id,$members)
    }

    # Resolve all merge chains to a *kept* root. An observed same-frame
    # collision in any two fragments is a safety veto. Lack of a sampled
    # collision does not prove full cluster-frame non-overlap.
    $components = [Collections.Generic.Dictionary[string,object]]::new(
        [StringComparer]::Ordinal)
    foreach ($row in $GroupRows) {
        if ([string]$row.Decision -notin @('Keep group','Merge group')) { continue }
        $chain = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $cursor = $row
        while ([string]$cursor.Decision -ceq 'Merge group') {
            $id = [string]$cursor.Group
            $dest = [string]$cursor.MergeInto
            if (-not $chain.Add($id) -or $dest -ceq $id -or
                -not $byId.ContainsKey($dest)) {
                throw 'Circular or missing reviewed merge target.'
            }
            $cursor = $byId[$dest]
        }
        if ([string]$cursor.Decision -cne 'Keep group') {
            throw 'A reviewed merge must terminate at a kept group.'
        }
        $rootId = [string]$cursor.Group
        if (-not $components.ContainsKey($rootId)) {
            $components[$rootId] = [Collections.Generic.List[object]]::new()
        }
        $components[$rootId].Add($row)
    }

    $persons = [Collections.Generic.Dictionary[string,object]]::new(
        [StringComparer]::Ordinal)
    $unknownRoots = 0
    $unlabelledRoots = 0
    $mergedCount = 0
    foreach ($rootId in $components.Keys) {
        $root = $byId[$rootId]
        $person = [string]$root.SelectedPerson
        $members = @($components[$rootId].ToArray())
        $frames = [Collections.Generic.HashSet[int]]::new()
        $hasMerge = @($members | Where-Object {
            [string]$_.Decision -ceq 'Merge group'
        }).Count -gt 0
        if ($hasMerge -and @($members | Where-Object {
            -not $verifiedFull.ContainsKey([string]$_.Group)
        }).Count -gt 0) {
            # Legacy source reports contain only representative crops.
            # A reviewed merged presence proposal cannot claim full
            # simultaneous-frame safety from that incomplete evidence.
            throw 'Reviewed merge requires complete frame membership for every fragment.'
        }
        foreach ($member in $members) {
            $memberPerson = [string]$member.SelectedPerson
            if ($memberPerson -cne '' -and $memberPerson -cne $person) {
                throw 'Reviewed merge contains conflicting identity evidence.'
            }
            # Complete member frames when available; representative-only
            # evidence is still sufficient for an UNMERGED single group.
            $memberId = [string]$member.Group
            $checkedFrames = if ($verifiedFull.ContainsKey($memberId)) {
                $verifiedFull[$memberId]
            } else {
                @($RepresentativeFrames[$memberId])
            }
            foreach ($frame in $checkedFrames) {
                if (-not $frames.Add([int]$frame)) {
                    throw 'Reviewed merge contains simultaneous source frames.'
                }
            }
            if ([string]$member.Decision -ceq 'Merge group') { $mergedCount++ }
        }
        if ($person -ceq 'UNKNOWN') {
            $unknownRoots++
        } elseif ($person -ceq '') {
            $unlabelledRoots++
        } else {
            if (-not $persons.ContainsKey($person)) {
                $persons[$person] = [pscustomobject]@{
                    Alias=$person; GroupRoots=0; GroupFragments=0
                    NoiseSamples=0; UncorroboratedSamples=0
                }
            }
            $persons[$person].GroupRoots++
            $persons[$person].GroupFragments += $members.Count
        }
    }

    $unreviewedAudit = 0
    $cannotTellAudit = 0
    $realUnlabelled = 0
    $realUnknown = 0
    $noiseReviewed = 0
    $uncorroboratedReviewed = 0
    foreach ($row in $AuditRows) {
        $source = [string]$row.Source
        $label = [string]$row.Label
        $person = [string]$row.PersonAlias
        if ($source -cnotin @('CenterFaceNoise','CenterFaceUncorroborated') -or
            $label -cnotin @('','Real face','Not a face','Cannot tell') -or
            ($person -cne '' -and
             ($label -cne 'Real face' -or
              ($person -cne 'UNKNOWN' -and $person -cnotmatch $knownPattern)))) {
            throw 'Invalid unassigned review classification.'
        }
        if ($label -ceq '') { $unreviewedAudit++; continue }
        if ($label -ceq 'Cannot tell') { $cannotTellAudit++; continue }
        if ($label -cne 'Real face') { continue }
        if ($source -ceq 'CenterFaceNoise') { $noiseReviewed++ }
        else { $uncorroboratedReviewed++ }
        if ($person -ceq '') {
            $realUnlabelled++
        } elseif ($person -ceq 'UNKNOWN') {
            # UNKNOWN is not a stable person key; never join UNKNOWN
            # samples, groups or videos as if they were one person.
            $realUnknown++
        } else {
            if (-not $persons.ContainsKey($person)) {
                $persons[$person] = [pscustomobject]@{
                    Alias=$person;GroupRoots=0;GroupFragments=0
                    NoiseSamples=0;UncorroboratedSamples=0
                }
            }
            if ($source -ceq 'CenterFaceNoise') {
                $persons[$person].NoiseSamples++
            } else {
                $persons[$person].UncorroboratedSamples++
            }
        }
    }
    $all = @($persons.Values | Sort-Object Alias)
    $groupPeople = @($all | Where-Object GroupRoots -GT 0).Count
    $auditOnly = @($all | Where-Object GroupRoots -EQ 0).Count
    $unresolvedGroup = @($GroupRows | Where-Object {
        [string]$_.Decision -in @('','Cannot tell','Split required')
    }).Count
    $metrics = [pscustomobject][ordered]@{
        SchemaVersion = 1
        KnownPersonPresenceProposals = $all.Count
        KnownPeopleFromRetainedGroups = $groupPeople
        KnownPeopleFromAuditsOnly = $auditOnly
        ReviewerKeptGroupRoots = $components.Count
        ReviewerMergeActions = $mergedCount
        FullFrameMemberGroupsAvailable = $verifiedFull.Count
        FullMemberMergeSafetyValidated = $true
        FullMemberMergeSafetyStatus = $(if ($mergedCount -gt 0) {
            'verified-complete-membership'
        } else { 'no-merges-to-verify' })
        UnknownGroupEvidenceUnits = $unknownRoots
        UnlabelledKeptGroupRoots = $unlabelledRoots
        OtherUnresolvedGroups = $unresolvedGroup
        UnreviewedAuditSamples = $unreviewedAudit
        CannotTellAuditSamples = $cannotTellAudit
        RealFaceAuditSamplesUnidentified = $realUnlabelled
        RealFaceAuditSamplesMarkedUnknown = $realUnknown
        RealFaceNoiseSamples = $noiseReviewed
        RealFaceUncorroboratedSamples = $uncorroboratedReviewed
        PersonCoverageProven = $false
        ProductionAssignmentsMade = 0
    }
    return [pscustomobject]@{
        Metrics=$metrics
        People=$all
    }
}
Export-ModuleMember -Function Resolve-VideoFacePresenceEvidence
