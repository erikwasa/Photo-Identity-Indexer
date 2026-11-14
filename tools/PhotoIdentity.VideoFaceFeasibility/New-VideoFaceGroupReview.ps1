<#
.SYNOPSIS
Create a PRIVATE offline review of experimental WI-0155 provisional groups.
Windows PowerShell 7.6. No database, inference, video access or writes to originals.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$PrivateReportPath,
  [Parameter(Mandatory)][string]$PrivateCasesPath,
  [ValidatePattern('^V(?!00)[0-9]{2}$')][string]$ClipAlias = 'V03',
  [ValidateSet('Provisional','ShadowDbscan')][string]$GroupSource = 'Provisional',
  [ValidateRange(240,640)][int]$CropLongEdge = 512,
  [switch]$OpenGallery
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or -not $env:LOCALAPPDATA) {
  throw 'Windows and LOCALAPPDATA are required.'
}
function LocalFile([string]$Path,[long]$Limit) {
  $file = Get-Item -LiteralPath ([IO.Path]::GetFullPath($Path)) -Force
  $blocked = [long][IO.FileAttributes]::ReparsePoint -bor
    [long][IO.FileAttributes]::Offline -bor [long]0x00040000 -bor [long]0x00400000
  if ($file.PSIsContainer -or $file.Length -lt 1 -or $file.Length -gt $Limit -or
      ([long]$file.Attributes -band $blocked) -ne 0 -or
      ((Get-Item -LiteralPath $file.DirectoryName).Attributes -band
        [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'A private input is missing, linked, offline or oversized.'
  }
  return $file.FullName
}
function Esc([object]$Value) { [Net.WebUtility]::HtmlEncode([string]$Value) }
$report = Get-Content -LiteralPath (LocalFile $PrivateReportPath (64MB)) -Raw |
  ConvertFrom-Json
$cases = Get-Content -LiteralPath (LocalFile $PrivateCasesPath (16KB)) -Raw |
  ConvertFrom-Json
$matches = @($report.clips | Where-Object alias -CEQ $ClipAlias)
$caseMatches = @($cases.clips | Where-Object alias -CEQ $ClipAlias)
if ($report.schemaVersion -ne 1 -or $cases.schemaVersion -ne 1 -or
    $matches.Count -ne 1 -or $caseMatches.Count -ne 1 -or
    @($cases.clips).Count -ne @($report.clips).Count) {
  throw 'Private report and cases must contain the requested video once.'
}
$clip = $matches[0]
$isShadow = $GroupSource -ceq 'ShadowDbscan'
$aliasPattern = if ($isShadow) { '^D(?!000)[0-9]{3}$' } else { '^G(?!000)[0-9]{3}$' }
$groupReview = if ($isShadow) {
  $clip.review.diagnostics.shadowDbscan
} else {
  $clip.review.diagnostics.provisionalGroups
}
if ($isShadow) {
  if ($null -eq $groupReview -or [int]$groupReview.schemaVersion -ne 1 -or
      [string]$groupReview.policyVersion -cne 'wi0155-video-shadow-dbscan-e018-min2-v1' -or
      [int]$groupReview.observationsEvaluated -ne [int]$clip.review.aggregate.facesDetected -or
      [int]$groupReview.observationsEvaluated -gt 5760 -or
      [int]$groupReview.clusterCount -lt 1 -or
      [int]$groupReview.clusterMembers -lt 1 -or
      [int]$groupReview.noiseObservations -lt 0 -or
      [int]$groupReview.clusterMembers + [int]$groupReview.noiseObservations -ne
        [int]$groupReview.observationsEvaluated -or
      [int]$groupReview.sameFrameCollisions -ne 0 -or
      [int]$groupReview.groupsRepresentedForReview -ne @($groupReview.groups).Count -or
      [int]$groupReview.groupsNotRepresentedForReview -ne
        [int]$groupReview.clusterCount - @($groupReview.groups).Count -or
      [int]$groupReview.membersInUnrepresentedGroups -lt 0 -or
      [int]$groupReview.membersInUnrepresentedGroups -gt [int]$groupReview.clusterMembers) {
    throw 'Shadow DBSCAN counts, constraints or policy are invalid.'
  }
} elseif ($null -eq $groupReview -or $groupReview.schemaVersion -ne 1 -or
    [int]$groupReview.maximumGroups -ne 64 -or
    [int]$groupReview.observationsConsidered -ne
      [int]$clip.review.aggregate.facesDetected) {
  throw 'The report lacks validated opt-in provisional groups.'
}
$groups = @($groupReview.groups)
# For new catalogue reports, require rank evidence for exactly these groups.
# Older reports without group suggestions remain valid and reviewable.
$rankings = @{}
$hasRankings = $isShadow -or (
  $null -ne $clip.review.diagnostics.PSObject.Properties['groupSuggestions'] -and
  $null -ne $clip.review.diagnostics.groupSuggestions)
if ($hasRankings) {
  $provided = if ($isShadow) {
    @($groups | ForEach-Object {
      [pscustomobject]@{
        groupAlias = $_.alias
        groupObservations = $_.observations
        representativeCount = @($_.representativeCrops).Count
        topCandidates = $_.topCandidates
      }
    })
  } else {
    @($clip.review.diagnostics.groupSuggestions)
  }
  if ($provided.Count -ne $groups.Count) {
    throw 'Group rank suggestions do not match group count.'
  }
  foreach ($item in $provided) {
    $id = [string]$item.groupAlias
    if ($id -cnotmatch $aliasPattern -or
        $rankings.ContainsKey($id) -or
        [int]$item.representativeCount -lt 1 -or
        [int]$item.representativeCount -gt 5 -or
        [int]$item.groupObservations -lt 1) {
      throw 'Invalid or duplicate private group ranks.'
    }
    $candidates = @($item.topCandidates)
    if ($candidates.Count -lt 1 -or $candidates.Count -gt 3) {
      throw 'Unexpected private group rank count.'
    }
    $aliases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $previous = [double]::PositiveInfinity
    foreach ($candidate in $candidates) {
      $alias = [string]$candidate.alias
      $score = [double]$candidate.score
      if ($alias -cnotmatch '^P([0-9]{2}|[0-9]{4})$' -or
          -not $aliases.Add($alias) -or
          -not [double]::IsFinite($score) -or
          $score -lt -1 -or $score -gt 1 -or $score -gt $previous) {
        throw 'Invalid or unsorted private top-three scores.'
      }
      $previous = $score
    }
    $rankings[$id] = $item
  }
}
if ($groups.Count -lt 1 -or $groups.Count -gt 64) {
  throw 'Expected 1 to 64 provisional groups.'
}
$total = ($groups | Measure-Object -Property observations -Sum).Sum
$hiddenObservations = if ($isShadow) {
  [int]$groupReview.noiseObservations + [int]$groupReview.membersInUnrepresentedGroups
} else { [int]$groupReview.observationsUnassigned }
if ($isShadow) {
  if ([int]$total -ne ([int]$groupReview.clusterMembers -
        [int]$groupReview.membersInUnrepresentedGroups) -or
      [int]$total + $hiddenObservations -ne
        [int]$groupReview.observationsEvaluated) {
    throw 'The shadow shortlist does not reconcile with all candidates.'
  }
} elseif ([int]$total -ne [int]$groupReview.observationsAssigned -or
    [int]$total + $hiddenObservations -ne
      [int]$groupReview.observationsConsidered) {
  throw 'Ungrouped observations are not correctly accounted for.'
}
$manifest = Get-Content -LiteralPath (
  LocalFile ([string]$caseMatches[0].manifest) (16KB)) -Raw |
    ConvertFrom-Json
$frames = @($manifest.frames)
if ($manifest.schemaVersion -ne 1 -or $frames.Count -lt 1 -or
    $frames.Count -gt 180 -or
    $frames.Count -ne [int]$clip.review.aggregate.framesExamined) {
  throw 'Approved source frame manifest is inconsistent.'
}
$frameFolder = Split-Path -Parent ([string]$caseMatches[0].manifest)
$rows = [Collections.Generic.List[object]]::new()
$cropList = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($group in $groups) {
  $alias = [string]$group.alias
  $crops = @($group.representativeCrops)
  if ($hasRankings) {
    if (-not $rankings.ContainsKey($alias) -or
        [int]$rankings[$alias].groupObservations -ne [int]$group.observations -or
        [int]$rankings[$alias].representativeCount -ne $crops.Count) {
      throw 'Private ranked group does not match its review crops.'
    }
  }
  if ($alias -cnotmatch $aliasPattern -or -not $seen.Add($alias) -or
      [int]$group.observations -lt 1 -or $crops.Count -lt 1 -or
      $crops.Count -gt 5 -or $crops.Count -gt [int]$group.observations) {
    throw 'Invalid or duplicate provisional group.'
  }
  $rows.Add([pscustomobject][ordered]@{
    Clip=$ClipAlias; Group=$alias; Observations=[int]$group.observations
    Decision=''; MergeInto=''; SelectedPerson=''; ReviewerNotes=''
  })
  $localSeen = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
  for ($i=0; $i -lt $crops.Count; $i++) {
    $crop=$crops[$i]
    $frame=[int]$crop.frameNumber
    $face=[int]$crop.faceNumber
    if ($frame -lt 1 -or $frame -gt $frames.Count -or
        $face -lt 1 -or $face -gt 32 -or
        -not $localSeen.Add(('{0}:{1}' -f $frame,$face)) -or
        [long]$crop.timestampMs -ne [long]$frames[$frame-1].timestampMs) {
      throw 'Unexpected private frame reference.'
    }
    $x=[double]$crop.x; $y=[double]$crop.y
    $w=[double]$crop.width; $h=[double]$crop.height
    if (-not ([double]::IsFinite($x) -and [double]::IsFinite($y) -and
        [double]::IsFinite($w) -and [double]::IsFinite($h)) -or
        $x -lt 0 -or $y -lt 0 -or $w -le 0 -or $h -le 0 -or
        $x + $w -gt 1.000001 -or $y + $h -gt 1.000001) {
      throw 'Invalid normalized face box.'
    }
    $fileName=[string]$frames[$frame-1].file
    if ($fileName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\.(jpg|jpeg|png)$') {
      throw 'Invalid approved source filename.'
    }
    $source = LocalFile (Join-Path $frameFolder $fileName) (8MB)
    $relative = ('crops/{0}-{1}-{2:D2}.png' -f $ClipAlias,$alias,($i+1))
    $cropList.Add([pscustomobject]@{
      Group=$alias; Source=$source; Box=$crop; Relative=$relative
    })
  }
}
$outputRoot=Join-Path $env:LOCALAPPDATA 'PhotoIdentity\WI0155\group-reviews'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (((Get-Item -LiteralPath $outputRoot).Attributes -band
    [IO.FileAttributes]::ReparsePoint) -ne 0) {
  throw 'Group review output directory must not be linked.'
}
$folder=Join-Path $outputRoot (
  $(if ($isShadow) { 'shadow-groups-' } else { 'groups-' }) +
  (Get-Date -Format 'yyyyMMdd-HHmmssfff') +
  '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $folder | Out-Null
New-Item -ItemType Directory -Path (Join-Path $folder 'crops') | Out-Null

Add-Type -AssemblyName System.Drawing
foreach ($item in $cropList) {
  $image=[Drawing.Image]::FromFile($item.Source)
  $bitmap=$null
  $graphics=$null
  try {
    $box=$item.Box
    $x=[int][Math]::Floor([double]$box.x*$image.Width)
    $y=[int][Math]::Floor([double]$box.y*$image.Height)
    $w=[Math]::Max(1,[int][Math]::Ceiling([double]$box.width*$image.Width))
    $h=[Math]::Max(1,[int][Math]::Ceiling([double]$box.height*$image.Height))
    $mx=[int][Math]::Ceiling($w*.25)
    $my=[int][Math]::Ceiling($h*.25)
    $left=[Math]::Max(0,$x-$mx)
    $top=[Math]::Max(0,$y-$my)
    $right=[Math]::Min($image.Width,$x+$w+$mx)
    $bottom=[Math]::Min($image.Height,$y+$h+$my)
    $cw=[Math]::Max(1,$right-$left)
    $ch=[Math]::Max(1,$bottom-$top)
    $scale=[Math]::Min(1.0,$CropLongEdge/[double][Math]::Max($cw,$ch))
    $outW=[Math]::Max(1,[int][Math]::Round($cw*$scale))
    $outH=[Math]::Max(1,[int][Math]::Round($ch*$scale))
    $bitmap=[Drawing.Bitmap]::new($outW,$outH)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImage($image,[Drawing.Rectangle]::new(0,0,$outW,$outH),
      [Drawing.Rectangle]::new($left,$top,$cw,$ch),
      [Drawing.GraphicsUnit]::Pixel)
    $dest=Join-Path $folder ($item.Relative.Replace('/',[IO.Path]::DirectorySeparatorChar))
    $bitmap.Save($dest,[Drawing.Imaging.ImageFormat]::Png)
  } finally {
    if ($null -ne $graphics) { $graphics.Dispose() }
    if ($null -ne $bitmap) { $bitmap.Dispose() }
    $image.Dispose()
  }
}
$js=LocalFile (Join-Path $PSScriptRoot 'VideoFaceGroupReview.js') (64KB)
Copy-Item -LiteralPath $js -Destination (
  Join-Path $folder 'group-review.js')
$worksheet=Join-Path $folder 'group-review-worksheet.csv'
$rows | Export-Csv -LiteralPath $worksheet -NoTypeInformation -Encoding utf8

$html=[Collections.Generic.List[string]]::new()
$intro = @'
<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; script-src 'self'">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>WI-0155 · Private provisional groups</title>
<style>
:root{color-scheme:dark}body{background:#101822;color:#e7f0f7;font:15px system-ui,Segoe UI,sans-serif;margin:0;padding:24px}
main{max-width:1200px;margin:auto}.note{color:#bacfdf;line-height:1.55}.toolbar{display:flex;gap:12px;align-items:center;flex-wrap:wrap;padding:15px 0}
button,select,input,textarea{font:inherit;color:#f0f5f9;background:#223446;border:1px solid #68839b;border-radius:5px;padding:7px}
button{background:#245d86;font-weight:bold;cursor:pointer}select,input,textarea{width:100%;box-sizing:border-box}
textarea{min-height:62px;resize:vertical}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:14px}
.group-card{border:1px solid #4c6479;border-radius:8px;padding:14px;background:#1a2b3c}
.group-card h2{font-size:19px;margin:0 0 4px}.photos{display:flex;gap:8px;overflow-x:auto;margin:12px 0}
.photos img{max-height:160px;max-width:100%;object-fit:contain;background:#08101a;border-radius:3px}
.group-card label{display:block;color:#c3d5e2;font-size:12px;margin:8px 0 3px}
.group-card small{color:#aebfcf}.warning{border-left:3px solid #f1bb61;padding-left:12px;margin:16px 0}
.group-card[hidden]{display:none}.ranked{background:#122536;border:1px solid #4b6479;padding:8px;border-radius:5px;margin-top:8px;font-size:13px}.ranked ol{margin:4px 0 0;padding-left:23px}.ranked li{margin:3px 0}.ranked small{font-weight:normal}.toolbar label{display:inline-flex;align-items:center;gap:6px;color:#d7e5f0}
.toolbar input[type=checkbox]{width:auto}.quick-dismiss{background:#60434c;margin-top:8px}
</style></head><body><main><h1>WI-0155 · Provisional groups</h1>
<p class="note">Each card contains a few representative detected-face crops from one provisional group. Groups are not confirmed people: multiple people can be incorrectly merged, one person can appear in several groups, and a group may contain no real face. Video face crops and embeddings are NEVER enrolled as future references.</p>
<p class="note">Review locally: Keep group, Merge group (enter the other G-number), Split required, Dismiss false detection, or Cannot tell. Use the one-click dismiss control for obvious background objects. SelectedPerson should be a private P01/P0001 alias (or UNKNOWN for a visible person without an eligible catalogue entry), never a real name. These controls only annotate a local CSV: they DO NOT merge, identify or modify anything in the database.</p>
<p class="note">Background objects can receive many detector hits, so observation count alone does NOT measure face-likeness. Optional view filters only hide cards temporarily, never drop evidence or remove rows from your saved CSV. Inspect lower-support groups too before declaring that a person is absent. Newer group reports contain up to five representative crops; older three-crop reports remain valid.</p>
<p class="note">Newer reports show top-three <b>pseudonym aliases</b> and exploratory cosine scores for each group, not names or verified identities. Low scores and small score gaps are uncertain; scores cannot tell whether a crop is a real face. Resolve aliases only with the separate local private people map. Older reports show no group ranks. No video timestamps are displayed.</p>
<div class="toolbar"><button id="save-groups" type="button">Save group-review CSV</button><button id="import-groups" type="button">Import saved CSV</button><label><input id="hide-dismissed" type="checkbox" checked> Hide dismissed</label><label><input id="repeat-only" type="checkbox"> Show groups with 3+ observations only</label><input id="groups-file" type="file" accept=".csv,text/csv" hidden><span id="group-status" aria-live="polite"></span></div>
'@
if ($isShadow) {
  $intro = $intro.Replace('Private provisional groups','Private shadow DBSCAN groups').
    Replace('Provisional groups','Shadow DBSCAN groups').
    Replace('provisional group','shadow DBSCAN cluster').
    Replace('G-number','D-number').
    Replace('G002','D002').
    Replace('Grouped','Clustered')
}
$html.Add($intro)
if ($isShadow) {
  $html.Add(('<p class="warning">Shadow comparison only: {0} clusters total, {1} displayed, {2} clusters ({3} observations) outside this shortlist, plus {4} noise observations. Noise may contain genuine singleton faces. The displayed groups are NOT full person coverage; the baseline grouping is unchanged. Never import shadow decisions as baseline review.</p>' -f
    [int]$groupReview.clusterCount,$groups.Count,
    [int]$groupReview.groupsNotRepresentedForReview,
    [int]$groupReview.membersInUnrepresentedGroups,
    [int]$groupReview.noiseObservations))
} elseif ($hiddenObservations -gt 0) {
  $html.Add(('<p class="warning">Group capacity reached: {0} candidate observations remain unassigned and may include real faces. This is not a complete person inventory.</p>' -f
    $hiddenObservations))
}
$html.Add('<div class="grid">')
foreach ($row in $rows) {
  $imgs=@($cropList | Where-Object Group -CEQ $row.Group)
  $pictures=($imgs | ForEach-Object {
    '<img loading="lazy" src="{0}" alt="Representative review crop">' -f (Esc $_.Relative)
  }) -join ''
  $rankHtml='<div class="ranked">Top-three catalogue suggestions not available in this report.</div>'
  if ($hasRankings) {
    $ranked=@($rankings[$row.Group].topCandidates)
    $list=($ranked | ForEach-Object {
      '<li>{0} — {1:F4}</li>' -f (Esc $_.alias),([double]$_.score)
    }) -join ''
    $best=[double]$ranked[0].score
    $margin=if ($ranked.Count -gt 1) {
      $best-[double]$ranked[1].score
    } else { $null }
    $status=if ($best -lt 0.8) {
      'Below exploratory review threshold'
    } elseif ($null -eq $margin) {
      'Single reference: uncertain'
    } elseif ($margin -lt 0.05) {
      'Ambiguous between top people'
    } else {
      'Potential review lead, NOT an identification'
    }
    $rankHtml='<div class="ranked"><strong>Top-{0} catalogue suggestions</strong><small> · {1}</small><ol>{2}</ol></div>' -f
      $ranked.Count,(Esc $status),$list
  }
  $html.Add(('<article class="group-card" data-clip="{0}" data-group="{1}" data-observations="{2}"><h2>{0} · {1}</h2><small>{2} observations · {3} crops</small><div class="photos">{4}</div>{5}<label>Decision</label><select class="decision"><option value="">Not reviewed</option><option>Keep group</option><option>Merge group</option><option>Dismiss false detection</option><option>Split required</option><option>Cannot tell</option></select><label>MergeInto (same-video group alias)</label><input class="merge-target" maxlength="4" placeholder="{6}"><label>SelectedPerson (optional pseudonym)</label><input class="person" maxlength="120"><label>ReviewerNotes</label><textarea class="notes" maxlength="1000"></textarea><button class="quick-dismiss" type="button">Dismiss obvious background</button></article>' -f
    (Esc $row.Clip),(Esc $row.Group),$row.Observations,$imgs.Count,$pictures,$rankHtml,
    $(if ($isShadow) { 'D002' } else { 'G002' })))
}
$html.Add('</div><p class="note">These are experimental candidate groups, not confirmed people. Export your review CSV before leaving; do not mix G-prefixed baseline and D-prefixed shadow worksheets.</p></main><script src="group-review.js"></script></body></html>')
$page=Join-Path $folder 'group-review.html'
$html -join [Environment]::NewLine |
  Set-Content -LiteralPath $page -Encoding utf8
if ($OpenGallery) { Invoke-Item -LiteralPath $page }
[pscustomobject]@{
  GalleryPath=$page
  WorksheetPath=$worksheet
  ReviewFolder=$folder
  Groups=$rows.Count
  Crops=$cropList.Count
  UnassignedObservations=$hiddenObservations
  GroupSource=$GroupSource
  NoiseObservations=$(if ($isShadow) { [int]$groupReview.noiseObservations } else { $null })
  GroupsNotRepresented=$(if ($isShadow) { [int]$groupReview.groupsNotRepresentedForReview } else { $null })
}
