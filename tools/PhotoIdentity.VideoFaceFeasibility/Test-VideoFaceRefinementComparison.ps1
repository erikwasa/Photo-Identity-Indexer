<#
.SYNOPSIS
Synthetic Windows-only regression for the private singleton-anchor comparison.
No real faces, inference, catalogue connection, or network.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function Assert([bool]$condition,[string]$message) { if (-not $condition) { throw $message } }
function Refused([scriptblock]$action) { try { & $action | Out-Null; return $false } catch { return $true } }
function JsonWrite($value,[string]$path) {
    [IO.File]::WriteAllText($path,($value | ConvertTo-Json -Depth 40),[Text.UTF8Encoding]::new($false))
}
function Crop([int]$frame,[long]$time,[int]$face=1) {
    return @{frameNumber=$frame;timestampMs=$time;faceNumber=$face;x=.15;y=.15;width=.35;height=.5;quality=.6;detectorConfidence=.8}
}
function Ranks([string]$a,[string]$b,[string]$c,[double]$score=.7) {
    return @(@{alias=$a;score=$score},@{alias=$b;score=.4},@{alias=$c;score=.2})
}
function Ranked([int]$frame,[long]$time,[int]$face,$top) {
    return @{frameNumber=$frame;timestampMs=$time;faceNumber=$face;topCandidates=@($top)}
}
function MakeGroup([string]$alias,$box) {
    return @{alias=$alias;observations=1;representativeCrops=@($box)}
}
$root=Join-Path $env:TEMP ('wi0155-refinement-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    Add-Type -AssemblyName System.Drawing
    $reportPath=Join-Path $root 'report.json'
    $mapPath=Join-Path $root 'map.json'
    $caseDir=Join-Path $root 'case-input'
    New-Item -ItemType Directory -Path $caseDir | Out-Null
    $casePath=Join-Path $caseDir 'cases.json'
    $v04=Join-Path $root 'v04.csv'
    $v06=Join-Path $root 'v06.csv'
    $map=@{schemaVersion=1;pgSnapshot='1:1:';people=@(
        @{alias='P0001';personId='00000000-0000-0000-0000-000000000001';displayName='PRIVATE-NAME-ONE'},
        @{alias='P0002';personId='00000000-0000-0000-0000-000000000002';displayName='PRIVATE-NAME-TWO'},
        @{alias='P0003';personId='00000000-0000-0000-0000-000000000003';displayName='PRIVATE-NAME-THREE'},
        @{alias='P0004';personId='00000000-0000-0000-0000-000000000004';displayName='PRIVATE-NAME-FOUR'}
    )}
    JsonWrite $map $mapPath
    $rowsA=@(
        [pscustomobject][ordered]@{Clip='V04';Group='G001';Observations=1;Decision='Keep group';MergeInto='';SelectedPerson='P0001';ReviewerNotes='PRIVATE-NOTE'},
        [pscustomobject][ordered]@{Clip='V04';Group='G002';Observations=1;Decision='Dismiss false detection';MergeInto='';SelectedPerson='';ReviewerNotes=''},
        [pscustomobject][ordered]@{Clip='V04';Group='G003';Observations=1;Decision='Keep group';MergeInto='';SelectedPerson='UNKNOWN';ReviewerNotes=''}
    )
    $rowsB=@([pscustomobject][ordered]@{Clip='V06';Group='G001';Observations=1;Decision='Keep group';MergeInto='';SelectedPerson='P0002';ReviewerNotes=''})
    $rowsA | Export-Csv -LiteralPath $v04 -Encoding utf8
    $rowsB | Export-Csv -LiteralPath $v06 -Encoding utf8
    $topA=Ranks P0003 P0004 P0002
    $topB=Ranks P0002 P0003 P0004
    $addedTop=Ranks P0001 P0003 P0004 .85
    $addedSecond=Ranks P0003 P0001 P0004 .75
    $passData=@(
        @{alias='V04';times=@(1000);descriptor=('a'*64);
            ranks=@((Ranked 1 1000 1 $topA),(Ranked 1 1000 2 $addedTop),(Ranked 1 1000 3 $topA));
            groups=@((MakeGroup G001 (Crop 1 1000 1)),(MakeGroup G002 (Crop 1 1000 2)),(MakeGroup G003 (Crop 1 1000 3)));
            audit=@()},
        @{alias='V05';times=@(1000,1500,2000,2500,3000);descriptor=('a'*64);
            ranks=@((Ranked 1 1000 1 $addedTop),(Ranked 2 1500 1 $addedTop),
                (Ranked 3 2000 1 $addedTop),(Ranked 4 2500 1 $addedSecond),
                (Ranked 5 3000 1 $topA));
            groups=@((MakeGroup G001 (Crop 1 1000)),(MakeGroup G002 (Crop 2 1500)));
            audit=@((Crop 4 2500))},
        @{alias='V06';times=@(1000);descriptor=('b'*64);
            ranks=@((Ranked 1 1000 1 $topB));
            groups=@((MakeGroup G001 (Crop 1 1000)));
            audit=@()},
        @{alias='V07';times=@(1000,1500);descriptor=('b'*64);
            ranks=@((Ranked 1 1000 1 $topB),(Ranked 2 1500 1 (Ranks P0001 P0003 P0004)));
            groups=@((MakeGroup G001 (Crop 1 1000)),(MakeGroup G002 (Crop 2 1500)));
            audit=@()}
    )
    $caseClips=@(); $reportClips=@()
    foreach ($p in $passData) {
        $pFolder=Join-Path $root $p.alias
        New-Item -ItemType Directory -Path $pFolder | Out-Null
        $frames=@();$frameDetails=@();$i=0
        foreach ($time in $p.times) {
            $i++
            $filename=('frame-{0:D3}.png' -f $i)
            $bitmap=[Drawing.Bitmap]::new(40,60)
            try { $bitmap.Save((Join-Path $pFolder $filename),[Drawing.Imaging.ImageFormat]::Png) }
            finally { $bitmap.Dispose() }
            $frames+=@{file=$filename;timestampMs=$time}
            $frameDetails+=@{frameNumber=$i;timestampMs=$time;facesDetected=@($p.ranks | Where-Object {$_.frameNumber -eq $i}).Count}
        }
        $manifest=Join-Path $pFolder 'frames.json'
        JsonWrite @{schemaVersion=1;frames=$frames} $manifest
        JsonWrite @{schemaVersion=1;samplingMode='Adaptive';sourceDescriptorSha256=$p.descriptor;samplingCoarseFrameCount=1} (Join-Path $pFolder 'extraction-metadata.json')
        $caseClips+=@{alias=$p.alias;manifest=$manifest}
        $rankSuggestions=@(
            foreach($g in $p.groups) {
                $crop=$g.representativeCrops[0]
                $rank=@($p.ranks | Where-Object {$_.frameNumber -eq $crop.frameNumber -and $_.faceNumber -eq $crop.faceNumber})[0]
                @{groupAlias=$g.alias;groupObservations=1;representativeCount=1;topCandidates=$rank.topCandidates}
            }
        )
        $reportClips+=@{alias=$p.alias;review=@{
            aggregate=@{framesExamined=$frames.Count;facesDetected=$p.ranks.Count}
            diagnostics=@{
                rankedFaces=@($p.ranks);frameDetections=$frameDetails
                provisionalGroups=@{schemaVersion=1;maximumGroups=64;observationsConsidered=$p.ranks.Count;
                    observationsAssigned=$p.groups.Count;observationsUnassigned=$p.ranks.Count-$p.groups.Count;
                    groups=@($p.groups);unassignedAudit=@{sampledCrops=@($p.audit)}}
                groupSuggestions=$rankSuggestions
                shadowDbscan=@{groups=@();noiseAudit=@{sampledCrops=@()}}
                centerFaceCounterfactual=@{corroboratedOnlyDbscan=@{groups=@();noiseAudit=@{sampledCrops=@()}};notCorroboratedAudit=@{sampledCrops=@()};independentOnlyAudit=@{sampledCrops=@((Crop 1 999 32))}}
            }
        }}
    }
    $caseObject=@{schemaVersion=1;catalogueScope='all-eligible';clips=$caseClips}
    JsonWrite $caseObject $casePath
    $report=@{schemaVersion=1;catalogue=@{pgSnapshot='1:1:';selectedPeople=4;eligiblePeople=4;exemplarsLoaded=6};clips=$reportClips}
    JsonWrite $report $reportPath
    $script=Join-Path $PSScriptRoot 'New-VideoFaceRefinementComparison.ps1'
    $argsForTest=@{
        PrivateReportPath=$reportPath;PrivateCasesPath=$casePath;PrivatePeopleMapPath=$mapPath
        ReviewedV04CsvPath=$v04;ReviewedV06CsvPath=$v06;OutputDirectory=(Join-Path $root 'out')
    }
    $before=@($reportPath,$casePath,$mapPath,$v04,$v06) | ForEach-Object {(Get-FileHash -LiteralPath $_).Hash}
    $result=& $script @argsForTest
    Assert ($result.InputEvidenceValidated -and $result.ProductionAssignmentsMade -eq 0 -and -not $result.KnownPersonCoverageProven) 'Unsafe result status.'
    Assert ($result.Pairs.Count -eq 2) 'Expected independent pairs.'
    $a=$result.Pairs[0];$b=$result.Pairs[1]
    Assert ($a.CoarseKnownPeople -eq 1 -and $a.AddedFrames -eq 4 -and $a.AddedRankedObservations -eq 4) 'Coarse anchor or timestamp accounting failed.'
    Assert ($a.PeopleWithAddedTopThreeLead -eq 1 -and $a.PeopleWithReviewableLead -eq 1 -and
        $a.PersonLeadOccurrencesWithoutCrop -eq 1 -and $a.ShortlistedLeads -eq 2) 'Missing geometry or duplicate timestamp handling failed.'
    Assert ($b.CoarseKnownPeople -eq 1 -and $b.PeopleWithAddedTopThreeLead -eq 0 -and $b.ShortlistedLeads -eq 0) 'Wrong-video or coarse-frame match accepted.'
    $html=Get-Content -LiteralPath $result.GalleryPath -Raw
    Assert ($html.Contains('data-rank-improved="1"') -and $html.Contains('data-score-improved="0"') -and
        $html.Contains('No crop-backed added-frame top-three lead available')) 'Missing rank/score distinction or no-lead card.'
    Assert (-not $html.Contains('PRIVATE-NAME') -and -not $html.Contains('PRIVATE-NOTE') -and
        -not $html.Contains('00000000-0000') -and -not $html.Contains($root)) 'Private source data leaked into generated gallery.'
    Assert (@(Get-ChildItem -LiteralPath (Join-Path $argsForTest.OutputDirectory 'crops') -File).Count -eq 4) 'Unexpected crop output count.'
    $aggregate=$result.Pairs | ConvertTo-Json -Depth 5
    Assert (-not $aggregate.Contains('P0001') -and -not $aggregate.Contains('PRIVATE')) 'Person data leaked to aggregates.'
    $after=@($reportPath,$casePath,$mapPath,$v04,$v06) | ForEach-Object {(Get-FileHash -LiteralPath $_).Hash}
    Assert (($before -join '|') -ceq ($after -join '|')) 'Input evidence changed.'
    Assert (Refused { & $script @argsForTest }) 'Existing output accepted.'
    $argsForTest.OutputDirectory=Join-Path $root 'limited'
    $limited=& $script @argsForTest -LeadsPerPerson 1
    Assert ($limited.Pairs[0].ShortlistedLeads -eq 1 -and $limited.Pairs[0].PersonLeadOccurrencesWithoutCrop -eq 1) 'Bound not enforced.'
    $argsForTest.OutputDirectory=Join-Path $root 'reject'
    $metaPath=Join-Path $root 'V05/extraction-metadata.json'
    JsonWrite @{schemaVersion=1;samplingMode='Adaptive';sourceDescriptorSha256=('c'*64);samplingCoarseFrameCount=1} $metaPath
    Assert (Refused { & $script @argsForTest }) 'Wrong source accepted.'
    Assert (-not (Test-Path -LiteralPath $argsForTest.OutputDirectory)) 'Rejected input wrote output.'
    JsonWrite @{schemaVersion=1;samplingMode='Adaptive';sourceDescriptorSha256=('a'*64);samplingCoarseFrameCount=1} $metaPath
    $rowsA[0].Decision='Merge group';$rowsA[0].MergeInto='G001'
    $rowsA | Export-Csv -LiteralPath $v04 -Encoding utf8
    Assert (Refused { & $script @argsForTest }) 'Invalid coarse review accepted.'
    Write-Host 'WI-0155 offline refinement comparison synthetic tests: PASS'
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
