<#
.SYNOPSIS
Synthetic counterfactual priority tests. No real media/identities, model or DB.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Check([bool]$Ok, [string]$Why) { if (-not $Ok) { throw $Why } }
function Rejects([scriptblock]$Run) {
    try { & $Run | Out-Null; return $false }
    catch { return $true }
}
$folder = Join-Path $env:TEMP ('wi0155-priority-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
try {
    $csv = Join-Path $folder 'review.csv'
    $json = Join-Path $folder 'full-catalogue.json'
    $mapFile = Join-Path $folder 'private-map.json'
    $script = Join-Path $PSScriptRoot 'Compare-VideoFaceReviewPriority.ps1'
    $rows = @(
        [pscustomobject][ordered]@{Clip='V03';Group='G001';Observations=1;Decision='Keep group';MergeInto='';SelectedPerson='P0001';ReviewerNotes=''},
        [pscustomobject][ordered]@{Clip='V03';Group='G002';Observations=4;Decision='Dismiss false detection';MergeInto='';SelectedPerson='';ReviewerNotes=''},
        [pscustomobject][ordered]@{Clip='V03';Group='G003';Observations=1;Decision='Keep group';MergeInto='';SelectedPerson='P0002';ReviewerNotes=''},
        [pscustomobject][ordered]@{Clip='V03';Group='G004';Observations=3;Decision='Dismiss false detection';MergeInto='';SelectedPerson='';ReviewerNotes=''},
        [pscustomobject][ordered]@{Clip='V03';Group='G005';Observations=1;Decision='Keep group';MergeInto='';SelectedPerson='UNKNOWN';ReviewerNotes=''}
    )
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
    $map = @{
        schemaVersion=1;pgSnapshot='1:1:'
        people=@(
            @{alias='P0001';personId='00000000-0000-0000-0000-000000000001';displayName='Private one'},
            @{alias='P0002';personId='00000000-0000-0000-0000-000000000002';displayName='Private two'},
            @{alias='P0003';personId='00000000-0000-0000-0000-000000000003';displayName='Private three'},
            @{alias='P0004';personId='00000000-0000-0000-0000-000000000004';displayName='Private four'}
        )
    }
    $map | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $mapFile -Encoding utf8

    $fixtures = @(
        @{Alias='G001';N=1;Frame=1;Conf=.4;Quality=.2;Scores=@(.38,.37,.24);Aliases=@('P0003','P0001','P0002')},
        @{Alias='G002';N=4;Frame=2;Conf=.95;Quality=.9;Scores=@(.70,.60,.3);Aliases=@('P0003','P0002','P0001')},
        @{Alias='G003';N=1;Frame=3;Conf=.75;Quality=.7;Scores=@(.24,.21,.12);Aliases=@('P0001','P0003','P0004')},
        @{Alias='G004';N=3;Frame=5;Conf=.8;Quality=.75;Scores=@(.50,.4,.3);Aliases=@('P0003','P0001','P0002')},
        @{Alias='G005';N=1;Frame=7;Conf=.65;Quality=.6;Scores=@(.40,.32,.21);Aliases=@('P0004','P0001','P0003')}
    )
    $groups = @(
        foreach ($f in $fixtures) {
            @{alias=$f.Alias;observations=$f.N;representativeCrops=@(
                @{frameNumber=$f.Frame;timestampMs=($f.Frame*1000L);faceNumber=1;
                  x=.1;y=.1;width=.2;height=.2;
                  detectorConfidence=$f.Conf;quality=$f.Quality}
            )}
        }
    )
    $ranked = @(
        foreach ($f in $fixtures) {
            @{groupAlias=$f.Alias;groupObservations=$f.N;representativeCount=1;topCandidates=@(
                foreach ($i in 0..2) {
                    @{alias=$f.Aliases[$i];score=$f.Scores[$i]}
                }
            )}
        }
    )
    $report = @{
        schemaVersion=1
        catalogue=@{pgSnapshot='1:1:';selectedPeople=4;eligiblePeople=4;exemplarsLoaded=8}
        clips=@(@{alias='V03';review=@{
            aggregate=@{facesDetected=14;framesExamined=8}
            diagnostics=@{
                provisionalGroups=@{
                    schemaVersion=1;maximumGroups=64
                    observationsConsidered=14;observationsAssigned=10
                    observationsUnassigned=4;groups=$groups
                }
                groupSuggestions=$ranked
            }
        }})
    }
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $json -Encoding utf8

    $args = @{
        ReviewedCsvPath=$csv
        PrivateGroupReportPath=$json
        PrivatePeopleMapPath=$mapFile
        ClipAlias='V03'
        ReviewBudgets=@(1,2,3,5)
    }
    $result = @(& $script @args)
    Check ($result.Count -eq 24) 'Expected six policies and four review budgets.'
    Check (@($result | Where-Object { $_.ReviewBudgetGroups -eq 5 }).Count -eq 6) 'Missing all-group comparisons.'
    foreach ($row in $result) {
        Check ($row.OriginalReviewedGroups -eq 5 -and $row.ReviewedTrueFaceGroups -eq 3 -and
            $row.ReviewedBackgroundGroups -eq 2) 'Changed review denominator.'
        Check ($row.TrueFaceGroupsShown + $row.TrueFaceGroupsNotShown -eq 3 -and
            $row.BackgroundGroupsShown + $row.BackgroundGroupsAvoided -eq 2) 'Non-conserving shortlisted groups.'
        Check ($row.ObservationsAssigned -eq 10 -and
            $row.ObservationsUnassignedAndUnlabelled -eq 4) 'Unassigned evidence was lost.'
        Check ($row.HumanPersonLabelsComplete -eq $true) 'Complete human labels were lost.'
        if ($row.ReviewBudgetGroups -eq 5) {
            Check ($row.TrueFaceGroupsShown -eq 3 -and $row.BackgroundGroupsShown -eq 2 -and
                $row.DistinctKnownPeopleShown -eq 2 -and
                $row.DistinctKnownPeopleWithCorrectTop3Shown -eq 1) 'Full-budget control changed group or identity recall.'
        }
    }
    $arrivalOne = @($result | Where-Object {
        $_.Policy -eq 'Arrival order' -and $_.ReviewBudgetGroups -eq 1
    })[0]
    Check ($arrivalOne.TrueFaceGroupsShown -eq 1 -and
        $arrivalOne.DistinctKnownPeopleShown -eq 1) 'Arrival-order baseline incorrect.'
    $confOne = @($result | Where-Object {
        $_.Policy -eq 'Detector confidence' -and $_.ReviewBudgetGroups -eq 1
    })[0]
    Check ($confOne.TrueFaceGroupsShown -eq 0 -and
        $confOne.BackgroundGroupsShown -eq 1) 'High-confidence background must outrank low-confidence true face in this fixture.'

    # A missing human label means person-level recall is unknown, not zero.
    $rows[0].SelectedPerson = ''
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
    $partial = @(& $script @args)
    Check ($partial.Count -eq 24 -and
        $partial[0].HumanPersonLabelsComplete -eq $false -and
        $null -eq $partial[0].DistinctKnownPeopleShown -and
        $partial[0].ReviewedTrueFaceGroups -eq 3) 'Partially labelled groups incorrectly became negative examples.'
    $rows[0].SelectedPerson = 'P0001'
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8

    # Ambiguous/unreviewed groups must not be assumed to be background.
    $rows[4].Decision = ''
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
    Check (Rejects { & $script @args }) 'Unreviewed real-face group accepted.'
    $rows[4].Decision = 'Keep group'
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8

    $map.pgSnapshot='wrong'
    $map | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $mapFile -Encoding utf8
    Check (Rejects { & $script @args }) 'Cross-snapshot map was accepted.'

    $raw = $result | ConvertTo-Json -Depth 6
    foreach ($private in @('G001','P0001','Private one','00000000-0000','review.csv','V03')) {
        Check (-not $raw.Contains($private)) 'Private group or person evidence leaked to aggregate result.'
    }
    Write-Host 'WI-0155 offline review-priority counterfactual tests: PASS'
}
finally {
    if (Test-Path -LiteralPath $folder) {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
}
