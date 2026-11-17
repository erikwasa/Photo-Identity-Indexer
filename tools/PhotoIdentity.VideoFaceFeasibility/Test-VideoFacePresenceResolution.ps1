<#
.SYNOPSIS
Synthetic offline WI-0155 human-reviewed video-person presence tests.
No media, models, database, real people or file paths from the catalogue.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'VideoFacePresenceResolution.psm1') -Force
function Check([bool]$Condition,[string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Refused([scriptblock]$Action) {
    try { & $Action | Out-Null; return $false }
    catch { return $true }
}
function MakeGroup([string]$Id,[string]$Decision,[string]$Person,[string]$Merge='',[int]$Count=1) {
    [pscustomobject]@{
        Group=$Id;Decision=$Decision;SelectedPerson=$Person;MergeInto=$Merge;Observations=$Count
    }
}
function MakeAuditSample([string]$Source,[string]$Label,[string]$Person) {
    [pscustomobject]@{
        Source=$Source;Label=$Label;PersonAlias=$Person
    }
}
$groups = @(
    (MakeGroup 'C001' 'Keep group' 'P0001' '' 2),
    (MakeGroup 'C002' 'Merge group' 'P0001' 'C001' 2),
    (MakeGroup 'C003' 'Keep group' 'P0002'),
    (MakeGroup 'C004' 'Keep group' 'UNKNOWN'),
    (MakeGroup 'C005' 'Keep group' 'UNKNOWN'),
    (MakeGroup 'C006' 'Keep group' ''),
    (MakeGroup 'C007' 'Cannot tell' ''),
    (MakeGroup 'C008' 'Dismiss false detection' '')
)
$audit = @(
    (MakeAuditSample 'CenterFaceNoise' 'Real face' 'P0001'),
    (MakeAuditSample 'CenterFaceNoise' 'Real face' 'P0003'),
    (MakeAuditSample 'CenterFaceNoise' 'Real face' 'UNKNOWN'),
    (MakeAuditSample 'CenterFaceNoise' 'Cannot tell' ''),
    (MakeAuditSample 'CenterFaceUncorroborated' 'Real face' 'P0003'),
    (MakeAuditSample 'CenterFaceUncorroborated' 'Real face' ''),
    (MakeAuditSample 'CenterFaceUncorroborated' 'Not a face' ''),
    (MakeAuditSample 'CenterFaceUncorroborated' '' '')
)
$frames = @{
    C001=@(1);C002=@(2);C003=@(3);C004=@(4)
    C005=@(5);C006=@(6);C007=@(7);C008=@(8)
}
$complete = @{
    C001=@(1,9); C002=@(2,10)
}
$r = Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $complete
Check ($r.Metrics.KnownPersonPresenceProposals -eq 3 -and
    $r.Metrics.KnownPeopleFromRetainedGroups -eq 2 -and
    $r.Metrics.KnownPeopleFromAuditsOnly -eq 1 -and
    $r.Metrics.ReviewerKeptGroupRoots -eq 5 -and
    $r.Metrics.ReviewerMergeActions -eq 1 -and
    $r.Metrics.FullFrameMemberGroupsAvailable -eq 2 -and
    $r.Metrics.FullMemberMergeSafetyStatus -ceq 'verified-complete-membership' -and
    $r.Metrics.FullMemberMergeSafetyValidated -and
    $r.Metrics.UnknownGroupEvidenceUnits -eq 2 -and
    $r.Metrics.UnlabelledKeptGroupRoots -eq 1 -and
    $r.Metrics.OtherUnresolvedGroups -eq 1 -and
    $r.Metrics.UnreviewedAuditSamples -eq 1 -and
    $r.Metrics.CannotTellAuditSamples -eq 1 -and
    $r.Metrics.RealFaceAuditSamplesUnidentified -eq 1 -and
    $r.Metrics.RealFaceAuditSamplesMarkedUnknown -eq 1 -and
    -not $r.Metrics.PersonCoverageProven -and
    $r.Metrics.ProductionAssignmentsMade -eq 0) 'Unexpected reviewed-presence aggregation.'
$deduped = @($r.People | Where-Object Alias -CEQ 'P0001')
Check ($deduped.Count -eq 1 -and
    $deduped[0].GroupRoots -eq 1 -and
    $deduped[0].GroupFragments -eq 2 -and
    $deduped[0].NoiseSamples -eq 1 -and
    $deduped[0].UncorroboratedSamples -eq 0) 'A person was duplicated across reviewed C/N sources.'
$extra = @($r.People | Where-Object Alias -CEQ 'P0003')
Check ($extra.Count -eq 1 -and
    $extra[0].GroupRoots -eq 0 -and
    $extra[0].NoiseSamples -eq 1 -and
    $extra[0].UncorroboratedSamples -eq 1) 'N/U-only known person should be proposed once.'
Check (@($r.People | Where-Object Alias -CEQ 'UNKNOWN').Count -eq 0) 'Unknown people were falsely deduplicated.'

$bad = @($groups | ForEach-Object { $_.PSObject.Copy() })
$bad[1].SelectedPerson='P0002'
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $bad -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $complete
}) 'Contradictory merged identity was accepted.'
$bad = @($groups | ForEach-Object { $_.PSObject.Copy() })
$bad[0].SelectedPerson=''
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $bad -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $complete
}) 'Source person label was applied to an unlabelled keeper.'
$bad = @($groups | ForEach-Object { $_.PSObject.Copy() })
$bad[1].MergeInto='C002'
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $bad -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $complete
}) 'Self-merging groups were accepted.'
$bad = @($groups | ForEach-Object { $_.PSObject.Copy() })
$bad[0].SelectedPerson='P0001'
$bad[0].Decision='Merge group'
$bad[0].MergeInto='C002'
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $bad -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $complete
}) 'Cyclic group merges were accepted.'
$bad = @($groups | ForEach-Object { $_.PSObject.Copy() })
$bad[7].SelectedPerson='P0004'
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $bad -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $complete
}) 'Dismissed group created an identity.'
$badFrames = $frames.Clone()
$badFrames.C002 = @(1)
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $audit -RepresentativeFrames $badFrames -CompleteMemberFrames $complete
}) 'Co-present representative frame evidence was merged.'
$badFrames = $frames.Clone()
[void]$badFrames.Remove('C002')
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $audit -RepresentativeFrames $badFrames -CompleteMemberFrames $complete
}) 'Missing source representative evidence was accepted.'
$badAudit = @($audit | ForEach-Object { $_.PSObject.Copy() })
$badAudit[0].Label = 'Not a face'
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $badAudit -RepresentativeFrames $frames -CompleteMemberFrames $complete
}) 'A rejected face was used as a known-person association.'
# A legacy full-candidate report can still support a no-merge V01
# proposal; it cannot certify any saved manual merge.
$legacyNoMerge = @($groups | ForEach-Object { $_.PSObject.Copy() })
$legacyNoMerge[1].Decision='Keep group'
$legacyNoMerge[1].MergeInto=''
$noMerge = Resolve-VideoFacePresenceEvidence -GroupRows $legacyNoMerge -AuditRows $audit -RepresentativeFrames $frames
Check ($noMerge.Metrics.FullMemberMergeSafetyStatus -ceq 'no-merges-to-verify' -and
    $noMerge.Metrics.FullFrameMemberGroupsAvailable -eq 0) 'Legacy no-merge review must remain valid.'
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $audit -RepresentativeFrames $frames
}) 'Old report incorrectly certified an unproved manual merge.'
$oneMissing = $complete.Clone()
[void]$oneMissing.Remove('C002')
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $oneMissing
}) 'Partial membership falsely certified a merged group.'
$hiddenCollision = $complete.Clone()
$hiddenCollision.C002 = @(2,9)
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $hiddenCollision
}) 'Collision outside representative crops escaped the full check.'
$badCount = $complete.Clone()
$badCount.C002 = @(2)
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $badCount
}) 'Incomplete source-group frame count was accepted.'
$badOrder = $complete.Clone()
$badOrder.C002 = @(10,2)
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $badOrder
}) 'Unsorted full membership was accepted.'
$missingCrop = $complete.Clone()
$missingCrop.C002 = @(10,11)
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $missingCrop
}) 'Complete membership did not contain representative crop.'
$extraGroup = $complete.Clone()
$extraGroup.C999 = @(12)
Check (Refused {
    Resolve-VideoFacePresenceEvidence -GroupRows $groups -AuditRows $audit -RepresentativeFrames $frames -CompleteMemberFrames $extraGroup
}) 'Unexpected source-group membership was accepted.'
Write-Host 'WI-0155 synthetic reviewed presence resolution: PASS'
