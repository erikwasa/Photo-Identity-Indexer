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
    # C-prefixed gallery evidence must never mix with original D-group CSVs.
    $corroboratedGroup = @{
        alias='D001';observations=2
        representativeCrops=@($groups[0].representativeCrops)
        topCandidates=@($groups[0].topCandidates)
    }
    function ReviewCrop([int]$Frame,[int]$Face) {
        return @{
            frameNumber=$Frame;timestampMs=($Frame*1000);faceNumber=$Face
            x=.2;y=.2;width=.3;height=.4
            detectorConfidence=.85;quality=.4
        }
    }
    $cfNoiseSamples = @(
        (ReviewCrop 1 3), (ReviewCrop 3 1), (ReviewCrop 5 1)
    )
    $cfNotSamples = @(
        (ReviewCrop 1 4), (ReviewCrop 5 2)
    )
    $corroboratedShadow = @{
        schemaVersion=1;policyVersion='wi0155-video-shadow-dbscan-e018-min2-v1'
        maximumCosineDistance=.18;minimumSamples=2;minimumClusterSize=2
        observationsEvaluated=5;clusterCount=1;clusterMembers=2
        noiseObservations=3;notSameFrameConstraints=0
        groupsRepresentedForReview=1;groupsNotRepresentedForReview=0
        membersInUnrepresentedGroups=0;sameFrameCollisions=0
        groups=@($corroboratedGroup)
        noiseAudit=@{
            schemaVersion=1;totalUnassignedObservations=3;maximumReviewSamples=96
            temporalStrata=12;strataRepresented=3;sampledCrops=$cfNoiseSamples
        }
    }
    $counterfactual = @{
        schemaVersion=1;independentDetectorId='synthetic-centerface'
        independentDetectorSha256=('d'*64);minimumIntersectionOverUnion=.25
        framesChecked=5;retainedYuNetObservations=133;corroboratedAt030=5
        corroboratedAt050=4;notCorroboratedAt030=128
        corroboratedOnlyDbscan=$corroboratedShadow
        notCorroboratedAudit=@{
            schemaVersion=1;totalUnassignedObservations=128;maximumReviewSamples=96
            temporalStrata=12;strataRepresented=2;sampledCrops=$cfNotSamples
        }
    }
    $report.clips[0].review.diagnostics.centerFaceCounterfactual=$counterfactual
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

    $cfGallery = & $galleryCmd -PrivateReportPath $reportPath -PrivateCasesPath $casesPath `
        -ClipAlias V03 -GroupSource CenterFaceShadow
    $folders.Add($cfGallery.ReviewFolder)
    Check ($cfGallery.Groups -eq 1 -and $cfGallery.Crops -eq 1 -and
        $cfGallery.GroupSource -ceq 'CenterFaceShadow' -and
        $cfGallery.NonCorroboratedObservations -eq 128 -and
        $cfGallery.NoiseObservations -eq 3 -and
        $cfGallery.GroupsNotRepresented -eq 0) 'Incorrect CenterFace group gallery accounting.'
    $cfHtml = Get-Content -LiteralPath $cfGallery.GalleryPath -Raw
    Check ($cfHtml.Contains('C001') -and
        $cfHtml.Contains('crops/V03-C001-01.png') -and
        $cfHtml.Contains('128 were NOT corroborated') -and
        -not $cfHtml.Contains('crops/V03-D001-01.png')) 'CenterFace gallery not isolated.'
    $cfRows = @(Import-Csv -LiteralPath $cfGallery.WorksheetPath)
    Check ($cfRows.Count -eq 1 -and $cfRows[0].Group -ceq 'C001') 'Corroborated CSV alias incorrect.'
    $cfRows[0].Decision='Keep group'
    $cfRows[0].SelectedPerson='P0002'
    $cfCsv = Join-Path $private 'corroborated-reviewed.csv'
    $cfRows | Export-Csv -LiteralPath $cfCsv -NoTypeInformation -Encoding utf8
    $cfMeasureArgs=@{
        ReviewedCsvPath=$cfCsv;PrivateGroupReportPath=$reportPath
        ClipAlias='V03';GroupSource='CenterFaceShadow'
    }
    Write-Host 'Synthetic CenterFace C-group aggregate: START'
    $cfCounts = & $measureCmd @cfMeasureArgs
    Write-Host 'Synthetic CenterFace C-group aggregate: PASS'
    Check ($cfCounts.GroupSource -ceq 'CenterFaceShadow' -and
        $cfCounts.TotalGroups -eq 1 -and
        $cfCounts.AssignedGroupObservations -eq 2 -and
        $cfCounts.ObservationsUnassigned -eq 3) 'Corroborated group count mismatch.'
    Write-Host 'Synthetic CenterFace C-group ranks: START'
    $cfRanks = & $rankCmd @cfMeasureArgs -PrivatePeopleMapPath $mapPath
    Write-Host 'Synthetic CenterFace C-group ranks: PASS'
    Check ($cfRanks.InputEvidenceValidated -and
        $cfRanks.CorroboratedCandidateSubset -eq 5 -and
        $cfRanks.CorrectTop3Groups -eq 1) 'Corroborated-only rank mismatch.'
    Check (Refused { & $measureCmd -ReviewedCsvPath $cfCsv `
        -PrivateGroupReportPath $reportPath -ClipAlias V03 `
        -GroupSource ShadowDbscan }) 'C-group CSV accepted as original D-group.'
    Check (Refused { & $measureCmd -ReviewedCsvPath $cfCsv `
        -PrivateGroupReportPath $reportPath -ClipAlias V03 }) 'C-group CSV accepted as original G-group.'

    $auditCmd = Join-Path $PSScriptRoot 'New-VideoFaceUnassignedReview.ps1'
    $auditMeasureCmd = Join-Path $PSScriptRoot 'Measure-VideoFaceUnassignedReview.ps1'
    foreach ($auditSource in @('CenterFaceUncorroborated','CenterFaceNoise')) {
        $auditGallery=& $auditCmd -PrivateReportPath $reportPath `
            -PrivateCasesPath $casesPath -ClipAlias V03 -AuditSource $auditSource
        $folders.Add((Split-Path -Parent $auditGallery.GalleryPath))
        $expectedCount = if ($auditSource -ceq 'CenterFaceNoise') { 3 } else { 2 }
        $expectedTotal = if ($auditSource -ceq 'CenterFaceNoise') { 3 } else { 128 }
        $expectedPrefix = if ($auditSource -ceq 'CenterFaceNoise') { 'N' } else { 'U' }
        Check ($auditGallery.AuditSource -ceq $auditSource -and
            $auditGallery.SampledCrops -eq $expectedCount -and
            $auditGallery.TotalUnassignedObservations -eq $expectedTotal) 'Wrong independent audit selection.'
        $auditRows=@(Import-Csv -LiteralPath $auditGallery.WorksheetPath)
        Check ($auditRows.Count -eq $expectedCount -and
            $auditRows[0].Sample -ceq ($expectedPrefix+'001')) 'Independent sample-prefix mismatch.'
        $auditRows[0].Label='Real face'
        $reviewedAudit=Join-Path $private ("$auditSource-reviewed.csv")
        $auditRows | Export-Csv -LiteralPath $reviewedAudit -NoTypeInformation -Encoding utf8
        Write-Host "Synthetic private sample aggregate $auditSource : START"
        $auditStats=& $auditMeasureCmd -ReviewedCsvPath $reviewedAudit `
            -PrivateGroupReportPath $reportPath -ClipAlias V03 -AuditSource $auditSource
        Check ($auditStats.SourceEvidenceValidated -and
            $auditStats.AuditSource -ceq $auditSource -and
            $auditStats.RealFaceSamples -eq 1 -and
            $auditStats.TotalUnassignedObservations -eq $expectedTotal) 'Independent sample measurement mismatch.'
        $wrongSource = if ($auditSource -ceq 'CenterFaceNoise') {
            'CenterFaceUncorroborated'
        } else { 'CenterFaceNoise' }
        Check (Refused { & $auditMeasureCmd -ReviewedCsvPath $reviewedAudit `
            -PrivateGroupReportPath $reportPath -ClipAlias V03 -AuditSource $wrongSource }) 'Wrong independent sample class was accepted.'
    }

    Write-Host 'Synthetic group-first review workspace: START'
    $workspaceCmd = Join-Path $PSScriptRoot 'New-VideoFaceReviewWorkspace.ps1'
    $workspace = & $workspaceCmd -PrivateReportPath $reportPath -PrivateCasesPath $casesPath -ClipAlias V03 -SpotlightPerLane 2
    $folders.Add((Split-Path -Parent $workspace.GalleryPath))
    $folders.Add((Split-Path -Parent $workspace.NoiseGalleryPath))
    $folders.Add((Split-Path -Parent $workspace.UncorroboratedGalleryPath))
    Check ($workspace.SchemaVersion -eq 1 -and
        $workspace.ClipAlias -ceq 'V03' -and
        $workspace.GroupSource -ceq 'CenterFaceShadow' -and
        $workspace.Groups -eq 1 -and
        $workspace.NoiseSamples -eq 3 -and
        $workspace.NoiseNotRepresented -eq 0 -and
        $workspace.UncorroboratedSamples -eq 2 -and
        $workspace.UncorroboratedNotRepresented -eq 126 -and
        $workspace.SpotlightPerLane -eq 2) 'Group-first source totals do not reconcile.'
    $hub = Get-Content -LiteralPath $workspace.GalleryPath -Raw
    Check ($hub.Contains('id="lane-noise"') -and
        $hub.Contains('id="lane-uncorroborated"') -and
        $hub.Contains('Show 1 more sampled crops') -and
        $hub.Contains('#sample-N003') -and
        $hub.Contains('#sample-U002') -and
        $hub.Contains('128') -and
        $hub.Contains('Save group-review CSV') -and
        $hub.Contains('group-review.js') -and
        $hub.Contains('not represented') -and
        $hub.Contains('Top-3 catalogue suggestions')) 'Incomplete group-first workspace.'
    Check ((Get-Content -LiteralPath $workspace.NoiseGalleryPath -Raw).Contains(
        'id="sample-N003"') -and
        (Get-Content -LiteralPath $workspace.UncorroboratedGalleryPath -Raw).Contains(
        'id="sample-U002"')) 'Private audit sample anchors are missing.'
    Check (@(Import-Csv -LiteralPath $workspace.GroupWorksheetPath).Count -eq 1 -and
        @(Import-Csv -LiteralPath $workspace.NoiseWorksheetPath).Count -eq 3 -and
        @(Import-Csv -LiteralPath $workspace.UncorroboratedWorksheetPath).Count -eq 2) 'Group-first source workbooks were changed.'
    Write-Host 'Synthetic group-first review workspace: PASS'

    foreach ($file in $hashes.Keys) {
        Check ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ceq
            $hashes[$file]) 'Independent gallery modified a source frame.'
    }
    $cfBad = $counterfactual.Clone()
    $cfBad.corroboratedAt030 = 6
    $report.clips[0].review.diagnostics.centerFaceCounterfactual=$cfBad
    $badCfPath = Join-Path $private 'bad-corroboration.json'
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $badCfPath -Encoding utf8
    Check (Refused { & $galleryCmd -PrivateReportPath $badCfPath `
        -PrivateCasesPath $casesPath -ClipAlias V03 `
        -GroupSource CenterFaceShadow }) 'Malformed corroboration accepted.'
    Check (Refused { & $measureCmd -ReviewedCsvPath $cfCsv `
        -PrivateGroupReportPath $badCfPath -ClipAlias V03 `
        -GroupSource CenterFaceShadow }) 'Malformed corroboration accepted by measurement.'
    Check (Refused { & $workspaceCmd -PrivateReportPath $badCfPath -PrivateCasesPath $casesPath -ClipAlias V03 }) 'Malformed corroboration accepted by group-first workspace.'
    $report.clips[0].review.diagnostics.centerFaceCounterfactual=$counterfactual

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
    Write-Host 'Synthetic original D-group aggregate: START'
    $counts = & $measureCmd @argsForMeasure
    Write-Host 'Synthetic original D-group aggregate: PASS'
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
