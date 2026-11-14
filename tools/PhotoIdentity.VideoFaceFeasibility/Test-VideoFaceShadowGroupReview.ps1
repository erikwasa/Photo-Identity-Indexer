<#
.SYNOPSIS
Synthetic Windows PowerShell 7.6 WI-0155 shadow-group review verification.
No real video, person, model, database or network access.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Shadow group gallery tests require Windows.' }
Add-Type -AssemblyName System.Drawing
function Check([bool]$Condition,[string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Refused([scriptblock]$Action) {
    try { & $Action | Out-Null; return $false }
    catch { return $true }
}
$root = Join-Path $env:TEMP ('wi0155-shadow-gallery-test-' + [Guid]::NewGuid().ToString('N'))
$private = Join-Path $root 'private'
$framesDir = Join-Path $root 'frames'
New-Item -ItemType Directory -Path $private,$framesDir | Out-Null
$folders = [Collections.Generic.List[string]]::new()
try {
    $frames = [Collections.Generic.List[object]]::new()
    $hashes = @{}
    for ($i=1; $i -le 5; $i++) {
        $name = 'frame-{0:D2}.png' -f $i
        $file = Join-Path $framesDir $name
        $image = [Drawing.Bitmap]::new(160,100)
        try { $image.Save($file,[Drawing.Imaging.ImageFormat]::Png) }
        finally { $image.Dispose() }
        $hashes[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        $frames.Add(@{timestampMs=($i*1000);file=$name})
    }
    $manifestPath = Join-Path $framesDir 'frames.json'
    @{schemaVersion=1;frames=@($frames)} | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8
    $casesPath = Join-Path $private 'cases.json'
    @{schemaVersion=1;clips=@(
        @{alias='V03';manifest=$manifestPath},
        @{alias='V01';manifest=$manifestPath}
    )} | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath $casesPath -Encoding utf8

    $mapPath = Join-Path $private 'people-map.json'
    @{schemaVersion=1;pgSnapshot='synthetic:1';people=@(
        @{alias='P0001';personId='00000000-0000-0000-0000-000000000001'},
        @{alias='P0002';personId='00000000-0000-0000-0000-000000000002'},
        @{alias='P0003';personId='00000000-0000-0000-0000-000000000003'},
        @{alias='P0004';personId='00000000-0000-0000-0000-000000000004'}
    )} | ConvertTo-Json -Depth 7 |
        Set-Content -LiteralPath $mapPath -Encoding utf8

    $groups = @(
        for ($i=0; $i -lt 64; $i++) {
            $frame = 1 + [int][Math]::Floor($i / 32)
            $face = 1 + ($i % 32)
            @{
                alias=('D{0:D3}' -f ($i+1));observations=2
                representativeCrops=@(@{
                    frameNumber=$frame;timestampMs=($frame*1000);faceNumber=$face
                    x=.15;y=.15;width=.3;height=.45
                    detectorConfidence=.9;quality=.5
                })
                topCandidates=@(
                    @{alias='P0001';score=.93},
                    @{alias='P0002';score=.85},
                    @{alias='P0003';score=.71}
                )
            }
        }
    )
    # 65th cluster is not represented, but two members are accounted for.
    $shadow = @{
        schemaVersion=1;policyVersion='wi0155-video-shadow-dbscan-e018-min2-v1'
        maximumCosineDistance=.18;minimumSamples=2;minimumClusterSize=2
        observationsEvaluated=133;clusterCount=65;clusterMembers=130
        noiseObservations=3;notSameFrameConstraints=200
        groupsRepresentedForReview=64;groupsNotRepresentedForReview=1
        membersInUnrepresentedGroups=2;sameFrameCollisions=0;groups=$groups
    }
    $report = @{
        schemaVersion=1
        catalogue=@{
            pgSnapshot='synthetic:1';selectedPeople=4;eligiblePeople=4;exemplarsLoaded=6
        }
        clips=@(
            @{
                alias='V03'
                review=@{
                    aggregate=@{framesExamined=5;facesDetected=133}
                    diagnostics=@{
                        shadowDbscan=$shadow
                        provisionalGroups=@{
                            schemaVersion=1;maximumGroups=64;observationsConsidered=133
                            observationsAssigned=2;observationsUnassigned=131
                            groups=@(@{
                                alias='G001';observations=2
                                representativeCrops=@($groups[0].representativeCrops)
                            })
                        }
                    }
                }
            },
            @{
                alias='V01'
                review=@{
                    aggregate=@{framesExamined=5;facesDetected=0}
                    diagnostics=@{shadowDbscan=$null;provisionalGroups=$null}
                }
            }
        )
    }
    $reportPath = Join-Path $private 'shadow.json'
    $report | ConvertTo-Json -Depth 20 |
        Set-Content -LiteralPath $reportPath -Encoding utf8

    $galleryCmd = Join-Path $PSScriptRoot 'New-VideoFaceGroupReview.ps1'
    $measureCmd = Join-Path $PSScriptRoot 'Measure-VideoFaceGroupReview.ps1'
    $rankCmd = Join-Path $PSScriptRoot 'Measure-VideoFaceGroupRankAccuracy.ps1'
    $galleryArgs = @{
        PrivateReportPath=$reportPath;PrivateCasesPath=$casesPath
        ClipAlias='V03';GroupSource='ShadowDbscan'
    }
    $gallery = & $galleryCmd @galleryArgs
    $folders.Add($gallery.ReviewFolder)
    Check ($gallery.GroupSource -ceq 'ShadowDbscan' -and
        $gallery.Groups -eq 64 -and $gallery.Crops -eq 64 -and
        $gallery.NoiseObservations -eq 3 -and $gallery.UnassignedObservations -eq 5 -and
        $gallery.GroupsNotRepresented -eq 1) 'Incorrect shadow gallery accounting.'
    $html = Get-Content -LiteralPath $gallery.GalleryPath -Raw
    Check ($html.Contains('D001') -and $html.Contains('crops/V03-D064-01.png') -and
        $html.Contains('65 clusters total') -and
        $html.Contains('3 noise observations') -and
        $html.Contains('Top-3 catalogue suggestions') -and
        $html.Contains('Never import shadow decisions as baseline review')) 'Shadow gallery incomplete.'
    $rows = @(Import-Csv -LiteralPath $gallery.WorksheetPath)
    Check ($rows.Count -eq 64 -and $rows[0].Group -ceq 'D001' -and
        $rows[63].Group -ceq 'D064') 'Wrong shadow CSV rows.'
    foreach ($file in $hashes.Keys) {
        Check ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ceq
            $hashes[$file]) 'Approved synthetic frame was modified.'
    }

    $baseline = & $galleryCmd -PrivateReportPath $reportPath -PrivateCasesPath $casesPath -ClipAlias V03
    $folders.Add($baseline.ReviewFolder)
    Check ($baseline.GroupSource -ceq 'Provisional' -and $baseline.Groups -eq 1 -and
        (Get-Content -LiteralPath $baseline.GalleryPath -Raw).Contains('G001')) 'Default baseline gallery changed.'

    # One known face has correct rank two. Another is absent from top three.
    # One group is unknown-to-the-catalogue and the other 61 are dismissed.
    $rows[0].Decision='Keep group'; $rows[0].SelectedPerson='P0002'
    $rows[1].Decision='Dismiss false detection'
    $rows[2].Decision='Keep group'; $rows[2].SelectedPerson='P0004'
    $rows[3].Decision='Keep group'; $rows[3].SelectedPerson='UNKNOWN'
    for ($i=4; $i -lt $rows.Count; $i++) {
        $rows[$i].Decision='Dismiss false detection'
    }
    $csv = Join-Path $private 'shadow-reviewed.csv'
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
    $argsForMeasure = @{
        ReviewedCsvPath=$csv;PrivateGroupReportPath=$reportPath
        ClipAlias='V03';GroupSource='ShadowDbscan'
    }
    $counts = & $measureCmd @argsForMeasure
    Check ($counts.TotalGroups -eq 64 -and $counts.GroupsReviewed -eq 64 -and
        $counts.AssignedGroupObservations -eq 128 -and
        $counts.ObservationsUnassigned -eq 5 -and
        $counts.ShadowNoiseObservations -eq 3 -and
        $counts.ShadowMembersOutsideReview -eq 2 -and
        $counts.ShadowTotalClusters -eq 65 -and
        $counts.DismissedGroups -eq 61 -and $counts.GroupCapacityReached) 'Wrong shadow group metrics.'

    $rank = & $rankCmd @argsForMeasure -PrivatePeopleMapPath $mapPath
    Check ($rank.InputEvidenceValidated -and
        $rank.LabelledKnownGroups -eq 2 -and $rank.LabelledUnknownGroups -eq 1 -and
        $rank.CorrectTop1Groups -eq 0 -and $rank.CorrectTop3Groups -eq 1 -and
        $rank.MissedTop3Groups -eq 1 -and
        $rank.DistinctKnownPeopleTop3 -eq 1 -and
        $rank.ShadowMembersOutsideReview -eq 2) 'Wrong shadow top-three metrics.'
    Check (Refused { & $measureCmd -ReviewedCsvPath $csv -PrivateGroupReportPath $reportPath -ClipAlias V03 }) 'D-groups accepted as G-groups.'
    $shadow.sameFrameCollisions = 1
    $badReport = Join-Path $private 'colliding.json'
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $badReport -Encoding utf8
    $invalidGalleryArgs = $galleryArgs.Clone()
    $invalidGalleryArgs.PrivateReportPath=$badReport
    $invalidCountArgs = $argsForMeasure.Clone()
    $invalidCountArgs.PrivateGroupReportPath=$badReport
    Check (Refused { & $galleryCmd @invalidGalleryArgs }) 'Co-present collision accepted.'
    Check (Refused { & $measureCmd @invalidCountArgs }) 'Co-present collision ignored in metrics.'
    $shadow.sameFrameCollisions = 0

    $shadow.membersInUnrepresentedGroups = 1
    $badReport = Join-Path $private 'missing-observation.json'
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $badReport -Encoding utf8
    $invalidGalleryArgs.PrivateReportPath=$badReport
    Check (Refused { & $galleryCmd @invalidGalleryArgs }) 'Unaccounted candidate was accepted.'
    $shadow.membersInUnrepresentedGroups = 2

    $groups[0].topCandidates[1].alias = 'P0001'
    $badReport = Join-Path $private 'duplicate-suggestion.json'
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $badReport -Encoding utf8
    $invalidGalleryArgs.PrivateReportPath=$badReport
    $invalidCountArgs.PrivateGroupReportPath=$badReport
    Check (Refused { & $galleryCmd @invalidGalleryArgs }) 'Duplicate catalogue alias was accepted.'
    Check (Refused { & $rankCmd @invalidCountArgs -PrivatePeopleMapPath $mapPath }) 'Duplicate rank was accepted.'

    Write-Host 'WI-0155 shadow DBSCAN gallery/rank synthetic tests: PASS'
}
finally {
    foreach ($folder in $folders) {
        if (Test-Path -LiteralPath $folder) {
            Remove-Item -LiteralPath $folder -Recurse -Force
        }
    }
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
