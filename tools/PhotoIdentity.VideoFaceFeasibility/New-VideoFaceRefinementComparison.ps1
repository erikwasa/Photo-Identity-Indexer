<#
.SYNOPSIS
Private, offline comparison of the four WI-0155 coarse/refined passes.
Only explicitly human-labelled, retained COARSE singleton observations
are anchors. Refined top-three matches are unverified leads, not identities.
No inference, database, source video, network, or catalogue writes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PrivateReportPath,
    [Parameter(Mandatory)][string]$PrivateCasesPath,
    [Parameter(Mandatory)][string]$PrivatePeopleMapPath,
    [Parameter(Mandatory)][string]$ReviewedV04CsvPath,
    [Parameter(Mandatory)][string]$ReviewedV06CsvPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1,3)][int]$LeadsPerPerson = 3,
    [switch]$OpenGallery
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows PowerShell 7.6 is required.' }
$stage = 'private-input'
$blocked = [long][IO.FileAttributes]::Offline -bor
    [long][IO.FileAttributes]::ReparsePoint -bor [long]0x00040000 -bor [long]0x00400000
function LocalDirectory([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if (-not $item.PSIsContainer) { throw 'Expected a directory.' }
    while ($null -ne $item) {
        if (([long]$item.Attributes -band $blocked) -ne 0) {
            throw 'Nonlocal or linked directory.'
        }
        $item = $item.Parent
    }
}
function LocalFile([string]$Path,[long]$Limit) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw 'Absolute private path required.' }
    $file = Get-Item -LiteralPath $Path -Force
    if ($file.PSIsContainer -or $file.Length -lt 1 -or $file.Length -gt $Limit -or
        ([long]$file.Attributes -band $blocked) -ne 0) { throw 'Unsafe file.' }
    LocalDirectory $file.DirectoryName
    return $file.FullName
}
function Within([string]$Parent,[string]$Child) {
    $relative = [IO.Path]::GetRelativePath($Parent,$Child)
    return -not ([IO.Path]::IsPathRooted($relative) -or
        $relative -eq '..' -or $relative.StartsWith('..\') -or $relative.StartsWith('../'))
}
function Html([object]$Value) { return [Net.WebUtility]::HtmlEncode([string]$Value) }
function Optional($Object,[string]$Name) {
    if ($null -ne $Object) {
        $property = $Object.PSObject.Properties[$Name]
        if ($null -ne $property) { return $property.Value }
    }
}
function Key($Item) { return ('{0}:{1}' -f [int]$Item.frameNumber,[int]$Item.faceNumber) }
function TargetRank($Rank,[string]$Person) {
    if ($null -eq $Rank) { return $null }
    $i = 0
    foreach ($candidate in $Rank.topCandidates) {
        $i++
        if ($candidate.alias -ceq $Person) {
            return [pscustomobject]@{Rank=$i;Score=[double]$candidate.score}
        }
    }
    return $null
}
function RankLabel($Hit) {
    if ($null -eq $Hit) { return 'Not in stored top three (score unknown)' }
    return ('Rank {0}, score {1:F4}' -f $Hit.Rank,$Hit.Score)
}
function AssertBox($Crop,$Pass) {
    $key = Key $Crop
    if (-not $Pass.Ranks.ContainsKey($key)) { throw 'Crop lacks a matching ranked face.' }
    $rank = $Pass.Ranks[$key]
    if ([long]$Crop.timestampMs -ne [long]$rank.timestampMs) {
        throw 'Crop timestamp mismatch.'
    }
    $x=[double]$Crop.x; $y=[double]$Crop.y
    $w=[double]$Crop.width; $h=[double]$Crop.height
    if (-not ([double]::IsFinite($x) -and [double]::IsFinite($y) -and
        [double]::IsFinite($w) -and [double]::IsFinite($h)) -or
        $x -lt 0 -or $y -lt 0 -or $w -le 0 -or $h -le 0 -or
        $x+$w -gt 1.000001 -or $y+$h -gt 1.000001) { throw 'Invalid crop geometry.' }
    if (-not [double]::IsFinite([double]$Crop.quality) -or
        [double]$Crop.quality -lt 0 -or [double]$Crop.quality -gt 1) {
        throw 'Invalid crop quality proxy.'
    }
    if ($Pass.Boxes.ContainsKey($key)) {
        $old = $Pass.Boxes[$key]
        foreach ($field in @('x','y','width','height')) {
            if ([Math]::Abs([double]$old.$field - [double]$Crop.$field) -gt 0.000001) {
                throw 'Conflicting stored YuNet boxes.'
            }
        }
    } else { $Pass.Boxes.Add($key,$Crop) }
}
function SaveCrop($Pass,$Box,[string]$File) {
    $frame = $Pass.Frames[[int]$Box.frameNumber-1]
    $source = LocalFile (Join-Path $Pass.Folder ([string]$frame.file)) (8MB)
    $image = [Drawing.Image]::FromFile($source)
    $bitmap=$null; $graphics=$null
    try {
        if ($image.Width -lt 1 -or $image.Height -lt 1 -or
            $image.Width -gt 16384 -or $image.Height -gt 16384 -or
            [long]$image.Width*$image.Height -gt 67108864) { throw 'Unexpected image dimensions.' }
        $left=[Math]::Max(0,[int][Math]::Floor(($Box.x-.25*$Box.width)*$image.Width))
        $top=[Math]::Max(0,[int][Math]::Floor(($Box.y-.25*$Box.height)*$image.Height))
        $right=[Math]::Min($image.Width,[int][Math]::Ceiling(($Box.x+1.25*$Box.width)*$image.Width))
        $bottom=[Math]::Min($image.Height,[int][Math]::Ceiling(($Box.y+1.25*$Box.height)*$image.Height))
        $w=[Math]::Max(1,$right-$left); $h=[Math]::Max(1,$bottom-$top)
        $scale=[Math]::Min(1.0,512/[double][Math]::Max($w,$h))
        $ow=[Math]::Max(1,[int][Math]::Round($w*$scale))
        $oh=[Math]::Max(1,[int][Math]::Round($h*$scale))
        $bitmap=[Drawing.Bitmap]::new($ow,$oh)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $graphics.DrawImage($image,[Drawing.Rectangle]::new(0,0,$ow,$oh),
            [Drawing.Rectangle]::new($left,$top,$w,$h),[Drawing.GraphicsUnit]::Pixel)
        $bitmap.Save($File,[Drawing.Imaging.ImageFormat]::Png)
    } finally {
        if ($null -ne $graphics) { $graphics.Dispose() }
        if ($null -ne $bitmap) { $bitmap.Dispose() }
        $image.Dispose()
    }
}
$created = $false
try {
    $reportFile = LocalFile $PrivateReportPath (64MB)
    $caseFile = LocalFile $PrivateCasesPath (16KB)
    $mapFile = LocalFile $PrivatePeopleMapPath (1MB)
    $reviews = @{
        V04=(LocalFile $ReviewedV04CsvPath (256KB))
        V06=(LocalFile $ReviewedV06CsvPath (256KB))
    }
    $repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    foreach ($privateFile in @($reportFile,$caseFile,$mapFile,$reviews.V04,$reviews.V06)) {
        if (Within $repo $privateFile) { throw 'Private evidence may not reside in the repository.' }
    }
    $report = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
    $cases = Get-Content -LiteralPath $caseFile -Raw | ConvertFrom-Json
    $map = Get-Content -LiteralPath $mapFile -Raw | ConvertFrom-Json
    $aliases = @('V04','V05','V06','V07')
    if ($report.schemaVersion -ne 1 -or $cases.schemaVersion -ne 1 -or
        $cases.catalogueScope -cne 'all-eligible' -or
        @($report.clips).Count -ne 4 -or @($cases.clips).Count -ne 4) {
        throw 'Expected the four-pass all-eligible report and cases.'
    }
    # These readers validate reviewed row/group membership, merge-chain shape,
    # retained labels and snapshot-matched private map. No human merge is applied.
    $stage = 'coarse-review-validation'
    $metrics = @{}
    foreach ($alias in @('V04','V06')) {
        $metrics[$alias] = & (Join-Path $PSScriptRoot 'Measure-VideoFaceGroupRankAccuracy.ps1') -ReviewedCsvPath $reviews[$alias] -PrivateGroupReportPath $reportFile -PrivatePeopleMapPath $mapFile -ClipAlias $alias -GroupSource Provisional
        if (-not $metrics[$alias].InputEvidenceValidated -or
            $metrics[$alias].UnassignedObservations -ne 0 -or
            $metrics[$alias].UnlabelledRetainedGroups -ne 0) {
            throw 'Coarse review must account for all observations and label retained groups.'
        }
    }
    $people = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($person in $map.people) { [void]$people.Add([string]$person.alias) }
    $stage = 'four-pass-binding'
    $passes = @{}
    $manifestPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($alias in $aliases) {
        $rc = @($report.clips | Where-Object alias -CEQ $alias)
        $cc = @($cases.clips | Where-Object alias -CEQ $alias)
        if ($rc.Count -ne 1 -or $cc.Count -ne 1) { throw 'Expected unique paired aliases.' }
        $manifestFile = LocalFile ([string]$cc[0].manifest) (16KB)
        if (-not $manifestPaths.Add($manifestFile) -or (Within $repo $manifestFile)) { throw 'Duplicate or repository-resident manifest.' }
        $folder = Split-Path -Parent $manifestFile
        $manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
        $meta = Get-Content -LiteralPath (LocalFile (Join-Path $folder 'extraction-metadata.json') (16KB)) -Raw | ConvertFrom-Json
        if ($manifest.schemaVersion -ne 1 -or $meta.schemaVersion -ne 1 -or
            $meta.samplingMode -cne 'Adaptive' -or
            [string]$meta.sourceDescriptorSha256 -cnotmatch '^[a-fA-F0-9]{64}$' -or
            @($manifest.frames).Count -lt 1 -or @($manifest.frames).Count -gt 180 -or
            @($manifest.frames).Count -ne [int]$rc[0].review.aggregate.framesExamined) {
            throw 'Manifest or source descriptor does not match the report.'
        }
        $frames = @($manifest.frames)
        $d = $rc[0].review.diagnostics
        $pass = [pscustomobject]@{
            Folder=$folder; Frames=$frames; Descriptor=[string]$meta.sourceDescriptorSha256
            Diagnostics=$d; CoarseCount=[int]$meta.samplingCoarseFrameCount
            Ranks=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            Boxes=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            Times=[Collections.Generic.HashSet[long]]::new()
        }
        $frameNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $frameDetections=@($d.frameDetections)
        if ($frameDetections.Count -ne $frames.Count) { throw 'Frame diagnostic count mismatch.' }
        $counts = @{}
        $previous=-1L
        for ($i=0;$i -lt $frames.Count;$i++) {
            $f=$frames[$i]; $fd=$frameDetections[$i]
            $ts=[long]$f.timestampMs
            if ($ts -le $previous -or $ts -gt 600000 -or
                [string]$f.file -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\.(png|jpg|jpeg)$' -or
                -not $frameNames.Add([string]$f.file) -or
                [int]$fd.frameNumber -ne $i+1 -or [long]$fd.timestampMs -ne $ts -or
                [int]$fd.facesDetected -lt 0 -or [int]$fd.facesDetected -gt 32) {
                throw 'Invalid frame diagnostic or manifest binding.'
            }
            [void]$pass.Times.Add($ts); $previous=$ts
            $counts[$i+1]=[int]$fd.facesDetected
        }
        foreach ($rank in $d.rankedFaces) {
            $n=[int]$rank.frameNumber; $face=[int]$rank.faceNumber
            if ($n -lt 1 -or $n -gt $frames.Count -or $face -lt 1 -or
                $face -gt $counts[$n] -or [long]$rank.timestampMs -ne [long]$frames[$n-1].timestampMs -or
                -not $pass.Ranks.TryAdd((Key $rank),$rank)) { throw 'Invalid ranked observation.' }
            $top=@($rank.topCandidates)
            $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $last=[double]::PositiveInfinity
            if ($top.Count -lt 1 -or $top.Count -gt 3) { throw 'Missing stored top-three ranks.' }
            foreach ($candidate in $top) {
                $score=[double]$candidate.score
                if (-not $people.Contains([string]$candidate.alias) -or
                    -not $seen.Add([string]$candidate.alias) -or
                    -not [double]::IsFinite($score) -or $score -lt -1 -or $score -gt 1 -or $score -gt $last) {
                    throw 'Invalid stored candidate rank.'
                }
                $last=$score
            }
        }
        if ($pass.Ranks.Count -ne [int]$rc[0].review.aggregate.facesDetected -or
            $pass.Ranks.Count -ne [int](($counts.Values | Measure-Object -Sum).Sum)) {
            throw 'Ranked face accounting mismatch.'
        }
        $cf=$d.centerFaceCounterfactual
        # Only YuNet geometry. I-only face ordinals belong to a different detector.
        foreach ($groupSource in @($d.provisionalGroups,$d.shadowDbscan,$cf.corroboratedOnlyDbscan)) {
            foreach ($g in @($groupSource.groups)) {
                foreach ($crop in $g.representativeCrops) { AssertBox $crop $pass }
            }
        }
        foreach ($audit in @(
            (Optional $d.provisionalGroups 'unassignedAudit'),
            (Optional $d.shadowDbscan 'noiseAudit'),
            (Optional $cf.corroboratedOnlyDbscan 'noiseAudit'),
            (Optional $cf 'notCorroboratedAudit')
        )) {
            if ($null -ne $audit) {
                foreach ($crop in $audit.sampledCrops) { AssertBox $crop $pass }
            }
        }
        $passes.Add($alias,$pass)
    }
    $stage='comparison-selection'
    $cards=[Collections.Generic.List[object]]::new()
    $summaries=[Collections.Generic.List[object]]::new()
    foreach ($pair in @(@('V04','V05'),@('V06','V07'))) {
        $coarse=$passes[$pair[0]]; $refined=$passes[$pair[1]]
        if ($coarse.Descriptor -cne $refined.Descriptor -or
            $coarse.CoarseCount -ne $coarse.Frames.Count -or
            $refined.CoarseCount -ne $coarse.Frames.Count -or
            -not $coarse.Times.IsSubsetOf($refined.Times)) { throw 'Wrong coarse/refined pairing.' }
        $groups=@{}
        foreach ($g in $coarse.Diagnostics.provisionalGroups.groups) {
            if ([int]$g.observations -ne 1 -or @($g.representativeCrops).Count -ne 1) {
                throw 'This comparison requires reviewed singleton coarse groups.'
            }
            $groups.Add([string]$g.alias,$g.representativeCrops[0])
        }
        $rows=@(Import-Csv -LiteralPath $reviews[$pair[0]])
        $anchors=@{}
        $anchorKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($row in $rows) {
            if ([string]$row.Decision -ceq '') { throw 'Coarse decisions must all be reviewed.' }
            $box=$groups[[string]$row.Group]
            $key=Key $box
            if (-not $anchorKeys.Add($key)) { throw 'Duplicate coarse singleton observation.' }
            if ($row.Decision -cin @('Keep group','Merge group') -and
                $row.SelectedPerson -cne 'UNKNOWN' -and $row.SelectedPerson) {
                $person=[string]$row.SelectedPerson
                if (-not $anchors.ContainsKey($person)) { $anchors[$person]=@() }
                $hit=TargetRank $coarse.Ranks[$key] $person
                $anchors[$person]+=[pscustomobject]@{Box=$box;Hit=$hit}
            }
        }
        if ($anchorKeys.Count -ne $coarse.Ranks.Count -or
            $anchors.Count -ne $metrics[$pair[0]].DistinctKnownPeopleLabelled) {
            throw 'Coarse reviewed anchor accounting mismatch.'
        }
        $newRanks=@($refined.Ranks.Values | Where-Object { -not $coarse.Times.Contains([long]$_.timestampMs) })
        $leadCount=0; $withSupport=0; $withoutGeometry=0; $withShortlist=0
        foreach ($person in @($anchors.Keys | Sort-Object)) {
            # Choose best available confirmed top-k anchor plus the clearest proxy
            # anchor. No identity label is inherited from a merge-chain destination.
            $a=@($anchors[$person] | Sort-Object @{Expression={if($null -ne $_.Hit){$_.Hit.Rank}else{4}}},@{Expression={if($null -ne $_.Hit){$_.Hit.Score}else{-2}};Descending=$true},@{Expression={$_.Box.quality};Descending=$true},@{Expression={$_.Box.frameNumber}},@{Expression={$_.Box.faceNumber}})
            $best=$a[0]
            $highestScore = @($a | Where-Object {$null -ne $_.Hit} |
                Sort-Object @{Expression={$_.Hit.Score};Descending=$true},
                    @{Expression={$_.Hit.Rank}},@{Expression={$_.Box.frameNumber}},
                    @{Expression={$_.Box.faceNumber}} | Select-Object -First 1)
            $scoreAnchor = if ($highestScore.Count -gt 0) { $highestScore[0] } else { $null }
            $clear=@($a | Sort-Object @{Expression={$_.Box.quality};Descending=$true},@{Expression={$_.Box.frameNumber}},@{Expression={$_.Box.faceNumber}})[0]
            $hits=@(foreach ($rank in $newRanks) {
                $hit=TargetRank $rank $person
                if ($null -ne $hit) {
                    $key=Key $rank
                    $box=if($refined.Boxes.ContainsKey($key)){$refined.Boxes[$key]}else{$null}
                    [pscustomobject]@{Observation=$rank;Hit=$hit;Box=$box}
                }
            })
            if ($hits.Count -gt 0) { $withSupport++ }
            $missing=@($hits | Where-Object {$null -eq $_.Box}).Count
            $withoutGeometry+=$missing
            $selected=[Collections.Generic.List[object]]::new()
            $times=[Collections.Generic.HashSet[long]]::new()
            foreach ($lead in @($hits | Where-Object {$null -ne $_.Box} |
                Sort-Object @{Expression={$_.Hit.Rank}},@{Expression={$_.Hit.Score};Descending=$true},@{Expression={$_.Box.quality};Descending=$true},@{Expression={$_.Observation.frameNumber}},@{Expression={$_.Observation.faceNumber}})) {
                if ($times.Add([long]$lead.Observation.timestampMs)) { $selected.Add($lead) }
                if ($selected.Count -ge $LeadsPerPerson) { break }
            }
            if ($selected.Count -gt 0) { $withShortlist++ }
            $leadCount+=$selected.Count
            $cards.Add([pscustomobject]@{
                CoarseAlias=$pair[0];RefinedAlias=$pair[1];Person=$person
                Coarse=$coarse;Refined=$refined;Best=$best;Clear=$clear;ScoreAnchor=$scoreAnchor
                AnchorCount=$a.Count;TotalHits=$hits.Count;MissingBoxes=$missing
                Leads=@($selected.ToArray())
            })
        }
        $summaries.Add([pscustomobject]@{
            CoarseAlias=$pair[0];RefinedAlias=$pair[1]
            CoarseKnownPeople=$anchors.Count;AddedFrames=$refined.Frames.Count-$coarse.Frames.Count
            AddedRankedObservations=$newRanks.Count
            PeopleWithAddedTopThreeLead=$withSupport
            PeopleWithReviewableLead=$withShortlist
            PersonLeadOccurrencesWithoutCrop=$withoutGeometry
            ShortlistedLeads=$leadCount
        })
    }
    if ($passes.V04.Descriptor -ceq $passes.V06.Descriptor -or $cards.Count -gt 128) {
        throw 'Expected separate physical videos and a bounded anchor count.'
    }
    $stage='private-output'
    if (-not [IO.Path]::IsPathFullyQualified($OutputDirectory)) { throw 'Absolute output required.' }
    $output=[IO.Path]::GetFullPath($OutputDirectory)
    $parent=Split-Path -Parent $output
    LocalDirectory $parent
    if ((Test-Path -LiteralPath $output) -or (Within $repo $output) -or
        (Within (Split-Path -Parent $caseFile) $output)) { throw 'Output must be new and outside repository and cases.' }
    foreach ($pass in $passes.Values) {
        if (Within $pass.Folder $output) { throw 'Output may not enter frame workspaces.' }
    }
    # All selection validation precedes image reconstruction.
    New-Item -ItemType Directory -Path $output | Out-Null
    $created=$true
    New-Item -ItemType Directory -Path (Join-Path $output 'crops') | Out-Null
    Add-Type -AssemblyName System.Drawing
    $reportHash=(Get-FileHash -LiteralPath $reportFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines=[Collections.Generic.List[string]]::new()
    $lines.Add(@'
<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'">
<title>WI-0155 private refinement comparison</title><style>
body{font:16px system-ui;background:#101820;color:#eef4f8;margin:24px}main{max-width:1200px;margin:auto}
section{border:1px solid #617587;border-radius:8px;padding:16px;margin:16px 0}
.grid{display:flex;flex-wrap:wrap;gap:18px}.crop{width:245px;border:1px solid #53697a;padding:10px}
img{display:block;max-width:240px;max-height:230px;object-fit:contain;background:#080e14;margin:8px 0}select,button{font:inherit;padding:7px;max-width:100%}
small{color:#b9cbd8}.note{color:#c9d9e3}#summary{white-space:pre-wrap}
</style></head><body><main><h1>Private coarse / added-frame comparison</h1>
<p class="note">Only explicitly human-labelled coarse singletons anchor this comparison. Added-frame top-three aliases are suggestions, NOT identifications. No stored score exists for an alias outside the top three. Missing crops, no leads and limited review cannot establish absence or recall. I-only candidates have no YuNet rank and are not included. A lead can appear for different suggested people; assess identity independently. No automatic associations or merges.</p>
<p>Compare the added crops against all confirmed coarse anchors shown (best rank, highest stored target score and strongest crop-quality proxy; duplicates omitted). Select whether the lead is the same person and visually clearer; do not equate a higher score with better identification.</p>
<button id="save" type="button">Save private review CSV</button><pre id="summary" aria-live="polite"></pre>
'@)
    $cardNo=0
    foreach ($card in $cards) {
        $cardNo++
        $lines.Add(('<section data-pair="{0}" data-person="{1}"><h2>{0} → {2} · {1}</h2><p>{3} confirmed coarse observation(s); {4} added top-three lead(s), {5} without stored geometry; {6} shown.</p><div class="grid">' -f
            (Html $card.CoarseAlias),(Html $card.Person),(Html $card.RefinedAlias),
            $card.AnchorCount,$card.TotalHits,$card.MissingBoxes,$card.Leads.Count))
        $anchorsToShow=@($card.Best)
        if ((Key $card.Best.Box) -cne (Key $card.Clear.Box)) { $anchorsToShow+=@($card.Clear) }
        if ($null -ne $card.ScoreAnchor -and
            (Key $card.ScoreAnchor.Box) -cne (Key $card.Best.Box) -and
            (Key $card.ScoreAnchor.Box) -cne (Key $card.Clear.Box)) {
            $anchorsToShow+=@($card.ScoreAnchor)
        }
        $j=0
        foreach ($anchor in $anchorsToShow) {
            $j++; $name=('a-{0:D3}-{1}.png' -f $cardNo,$j)
            SaveCrop $card.Coarse $anchor.Box (Join-Path $output ('crops/'+$name))
            $lines.Add(('<div class="crop"><strong>Confirmed coarse</strong><img src="crops/{0}" alt="Coarse reviewed crop"><small>{1}</small></div>' -f $name,(Html (RankLabel $anchor.Hit))))
        }
        $j=0
        foreach ($lead in $card.Leads) {
            $j++;$name=('r-{0:D3}-{1}.png' -f $cardNo,$j)
            SaveCrop $card.Refined $lead.Box (Join-Path $output ('crops/'+$name))
            $rankImproved=$null -eq $card.Best.Hit -or $lead.Hit.Rank -lt $card.Best.Hit.Rank
            $scoreImproved=$null -ne $card.ScoreAnchor -and $lead.Hit.Score -gt $card.ScoreAnchor.Hit.Score
            $lines.Add(('<div class="crop lead" data-refined="{0}" data-frame="{1}" data-face="{2}" data-rank="{3}" data-score="{4}" data-rank-improved="{5}" data-score-improved="{6}"><strong>Added frame — unverified</strong><img src="crops/{7}" alt="Refined candidate"><small>{8}</small><p><label>Human decision <select><option value="">Not reviewed</option><option>Same person - clearer</option><option>Same person - not clearer</option><option>Different person</option><option>Not a face</option><option>Cannot tell</option></select></label></p></div>' -f
                (Html $card.RefinedAlias),$lead.Observation.frameNumber,$lead.Observation.faceNumber,$lead.Hit.Rank,
                $lead.Hit.Score.ToString('R',[Globalization.CultureInfo]::InvariantCulture),
                ([int]$rankImproved),([int]$scoreImproved),$name,(Html (RankLabel $lead.Hit))))
        }
        if ($card.Leads.Count -eq 0) { $lines.Add('<p>No crop-backed added-frame top-three lead available. Other faces may still contain the person.</p>') }
        $lines.Add('</div></section>')
    }
    $lines.Add(('<meta name="report-sha256" content="{0}"><meta name="v04-review-sha256" content="{1}"><meta name="v06-review-sha256" content="{2}">' -f
        $reportHash,(Get-FileHash -LiteralPath $reviews.V04 -Algorithm SHA256).Hash.ToLowerInvariant(),
        (Get-FileHash -LiteralPath $reviews.V06 -Algorithm SHA256).Hash.ToLowerInvariant()))
    $lines.Add(@'
<script>
"use strict";
const cards=[...document.querySelectorAll("section")];
const leads=[...document.querySelectorAll(".lead")];
const summarize=()=>{
  const stats={};
  for(const card of cards){
    const p=card.dataset.pair;
    const s=stats[p]??={people:0,withLeads:0,leads:0,reviewed:0,same:0,clearer:0,betterRank:0,higherScore:0,wrong:0,background:0,uncertain:0};
    s.people++;
    const entries=[...card.querySelectorAll(".lead")]; if(entries.length)s.withLeads++;
    let same=false,clearer=false,betterRank=false,higherScore=false;
    for(const e of entries){
      s.leads++;
      const d=e.querySelector("select").value; if(d)s.reviewed++;
      if(d.startsWith("Same person")){same=true; clearer||=d.endsWith(" - clearer"); betterRank||=e.dataset.rankImproved==="1"; higherScore||=e.dataset.scoreImproved==="1";}
      if(d==="Different person")s.wrong++; if(d==="Not a face")s.background++; if(d==="Cannot tell")s.uncertain++;
    }
    s.same+=Number(same);s.clearer+=Number(clearer);s.betterRank+=Number(betterRank);s.higherScore+=Number(higherScore);
  }
  document.querySelector("#summary").textContent=Object.entries(stats).map(([p,s])=>
    p+": "+s.reviewed+"/"+s.leads+" leads reviewed; "+s.withLeads+"/"+s.people+" people have leads; "+
    s.same+" distinct confirmed same; "+s.clearer+" clearer; "+s.betterRank+" better stored rank; "+s.higherScore+" higher comparable score; "+
    s.wrong+" different-person, "+s.background+" background, "+s.uncertain+" uncertain leads.").join("\n");
};
document.addEventListener("change",summarize);summarize();
document.querySelector("#save").addEventListener("click",()=>{
  const q=v=>'"'+String(v??"").replaceAll('"','""')+'"';
  const h=name=>document.querySelector('meta[name="'+name+'"]').content;
  const out=[["ReportSha256","CoarseReviewSha256","Coarse","Refined","PersonAlias","Frame","Face","Rank","Score","HumanDecision"].map(q).join(",")];
  for(const e of leads){
    const c=e.closest("section"),d=e.dataset,p=c.dataset.pair;
    out.push([h("report-sha256"),h(p.toLowerCase()+"-review-sha256"),p,d.refined,c.dataset.person,d.frame,d.face,d.rank,d.score,e.querySelector("select").value].map(q).join(","));
  }
  const url=URL.createObjectURL(new Blob(["\uFEFF"+out.join("\r\n")+"\r\n"],{type:"text/csv;charset=utf-8"}));
  const a=document.createElement("a");a.href=url;a.download="wi0155-refinement-review-"+new Date().toISOString().replaceAll(":","-")+".csv";document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),2000);
});
</script></main></body></html>
'@)
    $page=Join-Path $output 'comparison.html'
    [IO.File]::WriteAllLines($page,$lines,[Text.UTF8Encoding]::new($false))
    $result=[pscustomobject]@{
        SchemaVersion=1;InputEvidenceValidated=$true;Pairs=@($summaries.ToArray())
        KnownPersonCoverageProven=$false;ProductionAssignmentsMade=0;GalleryPath=$page
    }
    if ($OpenGallery) { Invoke-Item -LiteralPath $page }
    $result
} catch {
    if ($created) { Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue }
    throw ('WI-0155 refinement comparison rejected inconsistent or unsafe inputs (stage: '+$stage+').')
}
