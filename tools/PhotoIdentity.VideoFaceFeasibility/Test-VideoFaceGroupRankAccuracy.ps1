<#
.SYNOPSIS
Deterministic private group-rank and false-detection audit fixtures for WI-0155.
No real videos, pictures, identities, ONNX models, PostgreSQL or network access.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-True([bool]$Condition,[string]$Description) {
    if (-not $Condition) { throw $Description }
}
function Refused([scriptblock]$Action) {
    try { & $Action | Out-Null; return $false }
    catch { return $true }
}
$root = Join-Path $env:TEMP ('wi0155-rank-audit-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $csv = Join-Path $root 'review.csv'
    $reportPath = Join-Path $root 'report.json'
    $mapPath = Join-Path $root 'people-map.json'
    $script = Join-Path $PSScriptRoot 'Measure-VideoFaceGroupRankAccuracy.ps1'
    $rows = @(
        [pscustomobject][ordered]@{Clip='V03';Group='G001';Observations=2;Decision='Keep group';MergeInto='';SelectedPerson='P0001';ReviewerNotes=''},
        [pscustomobject][ordered]@{Clip='V03';Group='G002';Observations=1;Decision='Merge group';MergeInto='G001';SelectedPerson='P0001';ReviewerNotes=''},
        [pscustomobject][ordered]@{Clip='V03';Group='G003';Observations=1;Decision='Keep group';MergeInto='';SelectedPerson='P0003';ReviewerNotes=''},
        [pscustomobject][ordered]@{Clip='V03';Group='G004';Observations=4;Decision='Dismiss false detection';MergeInto='';SelectedPerson='';ReviewerNotes=''},
        [pscustomobject][ordered]@{Clip='V03';Group='G005';Observations=1;Decision='Keep group';MergeInto='';SelectedPerson='UNKNOWN';ReviewerNotes=''}
    )
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
    $map = @{
        schemaVersion=1;pgSnapshot='1:1:'
        people=@(
            @{alias='P0001';personId='00000000-0000-0000-0000-000000000001';displayName='Private person one'},
            @{alias='P0002';personId='00000000-0000-0000-0000-000000000002';displayName='Private person two'},
            @{alias='P0003';personId='00000000-0000-0000-0000-000000000003';displayName='Private person three'},
            @{alias='P0004';personId='00000000-0000-0000-0000-000000000004';displayName='Private person four'}
        )
    }
    $map | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $mapPath -Encoding utf8

    $crops = @(
        @{frameNumber=1;timestampMs=1000;faceNumber=1;x=.1;y=.1;width=.2;height=.3;detectorConfidence=.82;quality=.52},
        @{frameNumber=3;timestampMs=3000;faceNumber=1;x=.1;y=.1;width=.2;height=.3;detectorConfidence=.88;quality=.58}
    )
    $items = @(
        @{alias='G001';observations=2;representativeCrops=@($crops)},
        @{alias='G002';observations=1;representativeCrops=@($crops[0])},
        @{alias='G003';observations=1;representativeCrops=@($crops[0])},
        @{alias='G004';observations=4;representativeCrops=@($crops[0],$crops[1])},
        @{alias='G005';observations=1;representativeCrops=@($crops[0])}
    )
    # A non-face group has a strong catalogue score; a real person has a miss.
    # This proves score/confidence are NOT reliable face-validity labels.
    $ranked = @(
        @{groupAlias='G001';groupObservations=2;representativeCount=2;topCandidates=@(
            @{alias='P0002';score=.92},@{alias='P0001';score=.90},@{alias='P0003';score=.50})},
        @{groupAlias='G002';groupObservations=1;representativeCount=1;topCandidates=@(
            @{alias='P0001';score=.95},@{alias='P0002';score=.8},@{alias='P0003';score=.4})},
        @{groupAlias='G003';groupObservations=1;representativeCount=1;topCandidates=@(
            @{alias='P0002';score=.85},@{alias='P0001';score=.4},@{alias='P0004';score=.3})},
        @{groupAlias='G004';groupObservations=4;representativeCount=2;topCandidates=@(
            @{alias='P0002';score=.97},@{alias='P0004';score=.88},@{alias='P0001';score=.5})},
        @{groupAlias='G005';groupObservations=1;representativeCount=1;topCandidates=@(
            @{alias='P0004';score=.9},@{alias='P0003';score=.7},@{alias='P0002';score=.5})}
    )
    $report = @{
        schemaVersion=1
        catalogue=@{pgSnapshot='1:1:';selectedPeople=4;eligiblePeople=4;exemplarsLoaded=6}
        clips=@(
            @{alias='V03';review=@{
                aggregate=@{facesDetected=14;framesExamined=3}
                diagnostics=@{
                    provisionalGroups=@{
                        schemaVersion=1;maximumGroups=64;observationsConsidered=14
                        observationsAssigned=9;observationsUnassigned=5;groups=$items
                    }
                    groupSuggestions=$ranked
                }
            }}
        )
    }
    $report | ConvertTo-Json -Depth 20 |
        Set-Content -LiteralPath $reportPath -Encoding utf8

    $argsForTest = @{
        ReviewedCsvPath=$csv
        PrivateGroupReportPath=$reportPath
        PrivatePeopleMapPath=$mapPath
        ClipAlias='V03'
    }
    $result = & $script @argsForTest
    Assert-True ($result.InputEvidenceValidated -and $result.ExemplarsLoaded -eq 6) 'Snapshot verification failed.'
    Assert-True ($result.TotalDetectedCandidates -eq 14 -and $result.AssignedObservations -eq 9 -and $result.UnassignedObservations -eq 5) 'Unassigned accounting failed.'
    Assert-True ($result.DismissedGroups -eq 1 -and $result.RetainedGroups -eq 4) 'Decision counts wrong.'
    Assert-True ($result.LabelledKnownGroups -eq 3 -and $result.LabelledUnknownGroups -eq 1 -and $result.UnlabelledRetainedGroups -eq 0) 'Human label counts wrong.'
    Assert-True ($result.CorrectTop1Groups -eq 1 -and $result.CorrectTop3Groups -eq 2 -and $result.MissedTop3Groups -eq 1) 'Group top-k ranking wrong.'
    Assert-True ($result.DistinctKnownPeopleLabelled -eq 2 -and $result.DistinctKnownPeopleTop1 -eq 1 -and $result.DistinctKnownPeopleTop3 -eq 1 -and $result.DistinctKnownPeopleMissedTop3 -eq 1) 'Person-level top-k ranking wrong.'
    Assert-True ($result.DismissedGroupsAboveExploratorySimilarity -eq 1 -and $result.DismissedGroupsWithExploratoryReviewLead -eq 1) 'High-scoring background was not accounted for.'
    Assert-True ($null -ne $result.MedianDismissedGroupDetectorConfidence -and $null -ne $result.MedianRetainedGroupQualityProxy) 'Source geometry proxy missing.'

    $out = $result | ConvertTo-Json -Depth 6
    foreach ($secret in @('G001','P0001','Private person','00000000-0000','review.csv','people-map.json','V03')) {
        Assert-True (-not $out.Contains($secret)) 'Private content leaked to aggregate output.'
    }

    # No labels means UNKNOWN accuracy, not a false zero-recall claim.
    foreach ($row in $rows) { $row.SelectedPerson = '' }
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
    $unlabelled = & $script @argsForTest
    Assert-True ($unlabelled.UnlabelledRetainedGroups -eq 4 -and
        $null -eq $unlabelled.CorrectTop1Groups -and
        $null -eq $unlabelled.DistinctKnownPeopleMissedTop3) 'Missing labels were counted as misses.'

    $rows[0].SelectedPerson = 'P9999'
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
    Assert-True (Refused { & $script @argsForTest }) 'Unknown person alias was accepted.'
    $rows[0].SelectedPerson = 'P0001'
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8

    $map.pgSnapshot = 'wrong'
    $map | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $mapPath -Encoding utf8
    Assert-True (Refused { & $script @argsForTest }) 'Cross-snapshot private map accepted.'
    $map.pgSnapshot = '1:1:'
    $map | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $mapPath -Encoding utf8

    $ranked[0].topCandidates[1].alias = 'P0002'
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Assert-True (Refused { & $script @argsForTest }) 'Duplicate person ranks accepted.'

    Write-Host 'WI-0155 private ranked-group audit synthetic tests: PASS'
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
