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
$groupReview = $clip.review.diagnostics.provisionalGroups
if ($null -eq $groupReview -or $groupReview.schemaVersion -ne 1 -or
    [int]$groupReview.maximumGroups -ne 64 -or
    [int]$groupReview.observationsConsidered -ne
      [int]$clip.review.aggregate.facesDetected) {
  throw 'The report lacks validated opt-in provisional groups.'
}
$groups = @($groupReview.groups)
if ($groups.Count -lt 1 -or $groups.Count -gt 64) {
  throw 'Expected 1 to 64 provisional groups.'
}
$total = ($groups | Measure-Object -Property observations -Sum).Sum
if ([int]$total -ne [int]$groupReview.observationsAssigned -or
    [int]$total + [int]$groupReview.observationsUnassigned -ne
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
  if ($alias -cnotmatch '^G(?!000)[0-9]{3}$' -or -not $seen.Add($alias) -or
      [int]$group.observations -lt 1 -or $crops.Count -lt 1 -or
      $crops.Count -gt 3 -or $crops.Count -gt [int]$group.observations) {
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
  'groups-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') +
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
$html.Add(@'
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
</style></head><body><main><h1>WI-0155 · Provisional groups</h1>
<p class="note">Each card contains a few representative detected-face crops from one provisional group. Groups are not confirmed people: multiple people can be incorrectly merged, one person can appear in several groups, and a group may contain no real face. Video face crops and embeddings are NEVER enrolled as future references.</p>
<p class="note">Review locally: Keep group, Merge group (enter the other G-number), Split required, Dismiss false detection, or Cannot tell. SelectedPerson is optional private text. These controls only annotate a local CSV: they DO NOT merge, identify or modify anything in the database.</p>
<p class="note">Full-catalogue top-3 identity suggestions are not yet implemented; the legacy experiment ranks against only five selected people. No video timestamps are displayed.</p>
<div class="toolbar"><button id="save-groups" type="button">Save group-review CSV</button><button id="import-groups" type="button">Import saved CSV</button><input id="groups-file" type="file" accept=".csv,text/csv" hidden><span id="group-status" aria-live="polite"></span></div>
'@)
if ([int]$groupReview.observationsUnassigned -gt 0) {
  $html.Add(('<p class="warning">Group capacity reached: {0} candidate observations remain unassigned and may include real faces. This is not a complete person inventory.</p>' -f
    [int]$groupReview.observationsUnassigned))
}
$html.Add('<div class="grid">')
foreach ($row in $rows) {
  $imgs=@($cropList | Where-Object Group -CEQ $row.Group)
  $pictures=($imgs | ForEach-Object {
    '<img loading="lazy" src="{0}" alt="Representative review crop">' -f (Esc $_.Relative)
  }) -join ''
  $html.Add(('<article class="group-card" data-clip="{0}" data-group="{1}" data-observations="{2}"><h2>{0} · {1}</h2><small>{2} observations · {3} crops</small><div class="photos">{4}</div><label>Decision</label><select class="decision"><option value="">Not reviewed</option><option>Keep group</option><option>Merge group</option><option>Dismiss false detection</option><option>Split required</option><option>Cannot tell</option></select><label>MergeInto (same-video group alias)</label><input class="merge-target" maxlength="4" placeholder="G002"><label>SelectedPerson (optional pseudonym)</label><input class="person" maxlength="120"><label>ReviewerNotes</label><textarea class="notes" maxlength="1000"></textarea></article>' -f
    (Esc $row.Clip),(Esc $row.Group),$row.Observations,$imgs.Count,$pictures))
}
$html.Add('</div><p class="note">The intended outcome is one manageable group per real person, not hundreds of face observations. Export your review CSV before leaving.</p></main><script src="group-review.js"></script></body></html>')
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
  UnassignedObservations=[int]$groupReview.observationsUnassigned
}
