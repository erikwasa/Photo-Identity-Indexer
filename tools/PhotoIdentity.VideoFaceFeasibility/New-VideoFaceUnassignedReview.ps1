<#
.SYNOPSIS
Create an offline, private crop-and-context gallery for the bounded WI-0155
unassigned-observation audit produced by --unassigned-audit.
.DESCRIPTION
Never runs models, opens videos, uses identities, connects to PostgreSQL or
changes original frames. This gallery reviews a *biased stratified audit
sample*, NOT all unassigned detections; its labels are not suppression rules.
Requires Windows PowerShell 7.6 because it uses System.Drawing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PrivateReportPath,
    [Parameter(Mandatory)][string]$PrivateCasesPath,
    [ValidatePattern('^V(?!00)[0-9]{2}$')][string]$ClipAlias = 'V03',
    [ValidateRange(240, 640)][int]$CropLongEdge = 512,
    [switch]$OpenGallery
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or -not $env:LOCALAPPDATA) {
    throw 'This private review requires Windows PowerShell 7.6 and LOCALAPPDATA.'
}

function LocalFile([string]$Path, [long]$Limit) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw 'A fully qualified private input is required.'
    }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $blocked = [long][IO.FileAttributes]::ReparsePoint -bor
        [long][IO.FileAttributes]::Offline -bor [long]0x00040000 -bor [long]0x00400000
    if ($item.PSIsContainer -or $item.Length -lt 1 -or $item.Length -gt $Limit -or
        ([long]$item.Attributes -band $blocked) -ne 0 -or
        ((Get-Item -LiteralPath $item.DirectoryName -Force).Attributes -band
            [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Private input must be an approved, regular, fully local file.'
    }
    return $item.FullName
}

function Esc([object]$Value) { return [Net.WebUtility]::HtmlEncode([string]$Value) }

$report = Get-Content -LiteralPath (LocalFile $PrivateReportPath (64MB)) -Raw |
    ConvertFrom-Json
$cases = Get-Content -LiteralPath (LocalFile $PrivateCasesPath (16KB)) -Raw |
    ConvertFrom-Json
$clips = @($report.clips | Where-Object { $_.alias -ceq $ClipAlias })
$matches = @($cases.clips | Where-Object { $_.alias -ceq $ClipAlias })
if ($report.schemaVersion -ne 1 -or $cases.schemaVersion -ne 1 -or
    @($report.clips).Count -ne @($cases.clips).Count -or
    $clips.Count -ne 1 -or $matches.Count -ne 1) {
    throw 'Audit report and private cases do not identify exactly one matching clip.'
}
$clip = $clips[0]
$review = $clip.review.diagnostics.provisionalGroups
$audit = $review.unassignedAudit
if ($null -eq $review -or $review.schemaVersion -ne 1 -or
    [int]$review.maximumGroups -ne 64 -or
    [int]$review.observationsConsidered -ne [int]$clip.review.aggregate.facesDetected -or
    [int]$review.observationsAssigned + [int]$review.observationsUnassigned -ne
      [int]$review.observationsConsidered -or
    $null -eq $audit -or [int]$audit.schemaVersion -ne 1 -or
    [int]$audit.maximumReviewSamples -ne 96 -or
    [int]$audit.temporalStrata -ne 12 -or
    [int]$audit.totalUnassignedObservations -ne [int]$review.observationsUnassigned) {
    throw 'Report does not contain a consistent bounded WI-0155 unassigned audit.'
}

$samples = @($audit.sampledCrops | Sort-Object timestampMs,frameNumber,faceNumber)
if ($samples.Count -lt 1 -or $samples.Count -gt 96 -or
    $samples.Count -gt [int]$audit.totalUnassignedObservations -or
    [int]$audit.strataRepresented -lt 1 -or
    [int]$audit.strataRepresented -gt 12) {
    throw 'Invalid private audit sample count.'
}
if (@($review.groups).Count -ne 64 -or
    [int]$review.observationsUnassigned -lt 1) {
    throw 'An unassigned capacity audit requires all 64 provisional groups.'
}
$assigned = 0
foreach ($group in @($review.groups)) {
    $assigned += [int]$group.observations
}
if ($assigned -ne [int]$review.observationsAssigned) {
    throw 'Original grouped observations do not reconcile.'
}

$manifestPath = LocalFile ([string]$matches[0].manifest) (16KB)
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$frames = @($manifest.frames)
if ($manifest.schemaVersion -ne 1 -or $frames.Count -lt 1 -or
    $frames.Count -gt 180 -or
    $frames.Count -ne [int]$clip.review.aggregate.framesExamined) {
    throw 'Private approved frame manifest does not match the audit.'
}
$frameFolder = Split-Path -Parent $manifestPath
$minTime = [long]$frames[0].timestampMs
$maxTime = [long]$frames[-1].timestampMs
if ($minTime -lt 0 -or $maxTime -gt 600000 -or $maxTime -lt $minTime) {
    throw 'Unbounded or invalid approved frame timeline.'
}

$rows = [Collections.Generic.List[object]]::new()
$cropTasks = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$strata = @{}
$perFrame = @{}
$priorTime = -1L

for ($i=0; $i -lt $samples.Count; $i++) {
    $crop = $samples[$i]
    $frame = [int]$crop.frameNumber
    $face = [int]$crop.faceNumber
    $time = [long]$crop.timestampMs
    $key = '{0}:{1}' -f $frame,$face
    $conf = [double]$crop.detectorConfidence
    $quality = [double]$crop.quality
    $x = [double]$crop.x; $y = [double]$crop.y
    $w = [double]$crop.width; $h = [double]$crop.height

    if ($frame -lt 1 -or $frame -gt $frames.Count -or
        $face -lt 1 -or $face -gt 32 -or -not $seen.Add($key) -or
        $time -lt $priorTime -or
        $time -ne [long]$frames[$frame - 1].timestampMs -or
        -not [double]::IsFinite($conf) -or $conf -lt 0 -or $conf -gt 1 -or
        -not [double]::IsFinite($quality) -or $quality -lt 0 -or $quality -gt 1 -or
        -not [double]::IsFinite($x) -or -not [double]::IsFinite($y) -or
        -not [double]::IsFinite($w) -or -not [double]::IsFinite($h) -or
        $x -lt 0 -or $y -lt 0 -or $w -le 0 -or $h -le 0 -or
        $x + $w -gt 1.000001 -or $y + $h -gt 1.000001) {
        throw 'Invalid, duplicated or mismatched unassigned detection geometry.'
    }
    $priorTime = $time
    $stratum = [int][Math]::Min(11,
        (($time - $minTime) * 12) / ($maxTime - $minTime + 1))
    $stratumKey = [string]$stratum
    $frameKey = '{0}:{1}' -f $stratum,$frame
    if (-not $strata.ContainsKey($stratumKey)) { $strata[$stratumKey]=0 }
    if (-not $perFrame.ContainsKey($frameKey)) { $perFrame[$frameKey]=0 }
    $strata[$stratumKey]++
    $perFrame[$frameKey]++
    if ($strata[$stratumKey] -gt 8 -or $perFrame[$frameKey] -gt 2) {
        throw 'Unassigned sample exceeds temporal or per-frame audit bounds.'
    }

    $filename = [string]$frames[$frame - 1].file
    if ($filename -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\.(jpg|jpeg|png)$') {
        throw 'Invalid approved private source frame filename.'
    }
    $file = LocalFile (Join-Path $frameFolder $filename) (8MB)
    $id = 'S{0:D3}' -f ($i + 1)
    $rows.Add([pscustomobject][ordered]@{
        Clip=$ClipAlias
        Sample=$id
        Frame=$frame
        Face=$face
        Label=''
        PersonAlias=''
    })
    $cropTasks.Add([pscustomobject]@{
        Id=$id; Path=$file; Box=$crop; Confidence=$conf; Quality=$quality
        Stratum=$stratum+1
    })
}
if ($strata.Count -ne [int]$audit.strataRepresented) {
    throw 'Private audit strata do not match declared review evidence.'
}

$outputRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\WI0155\unassigned-reviews'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (((Get-Item -LiteralPath $outputRoot -Force).Attributes -band
      [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Private unassigned-review output directory must be regular.'
}
$output = Join-Path $outputRoot (
    'audit-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $output | Out-Null
New-Item -ItemType Directory -Path (Join-Path $output 'crops') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $output 'context') | Out-Null

Add-Type -AssemblyName System.Drawing
$gallery = [Collections.Generic.List[string]]::new()
$gallery.Add(@'
<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; script-src 'self'">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>WI-0155 · Private unassigned candidate audit</title>
<style>
:root{color-scheme:dark}body{background:#101923;color:#e6edf3;font:15px/1.5 system-ui,Segoe UI,sans-serif;margin:0;padding:24px}
main{max-width:1380px;margin:auto}h1{font-size:1.5rem}.note{color:#c1d1df;max-width:1100px}
.toolbar{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:18px 0}
button,select,input{font:inherit;color:#f6f9fc;background:#223a50;border:1px solid #647f96;border-radius:6px;padding:8px}
button{cursor:pointer;background:#245e89;font-weight:600}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(275px,1fr));gap:14px}
article{border:1px solid #4c6378;border-radius:8px;background:#192b3d;padding:12px}
article h2{margin:0 0 5px;font-size:17px}.sample-meta{font-size:12px;color:#b6cad9}
.images{display:flex;gap:8px;align-items:center;margin:10px 0}
.images a{width:50%;height:160px;display:flex;align-items:center;justify-content:center;background:#0b1724}
.images img{max-width:100%;max-height:160px;object-fit:contain}
article label{display:block;font-size:13px;margin-top:8px}
article select,article input{width:100%;box-sizing:border-box}
article[hidden]{display:none}
</style></head><body><main data-clip="CLIP_ALIAS_TOKEN">
<h1>WI-0155 · Unassigned detections</h1>
<p class="note">PRIVATE, read-only human review of the bounded audit samples that did not join a provisional group after capacity was reached. These are NOT all unassigned detections, not independent random samples, not identified people and not a detection precision estimate. The temporal sampler deliberately favors higher quality-proxy examples. A real face here is evidence for missed reviewable material, not an automatic identity.</p>
<p class="note">Classify each crop as Real face, Not a face, or Cannot tell. If you recognize a real face, optionally enter its existing PRIVATE P0001-style alias or UNKNOWN. No real names, notes, raw frames, scores, or identity decisions leave this folder. CSV files remain local, and importing them never changes the database. Use the enlarged crop and context image to check tiny and oblique faces.</p>
<div class="toolbar"><button type="button" id="save-audit">Save reviewed CSV</button><button type="button" id="import-audit">Import reviewed CSV</button><label><input type="checkbox" id="hide-nonface"> Hide not-face samples</label><input type="file" id="audit-file" accept=".csv,text/csv" hidden><span id="audit-status" aria-live="polite"></span></div>
<div class="grid" id="audit-grid">
'@)

try {
    foreach ($task in $cropTasks) {
        $image = [Drawing.Image]::FromFile($task.Path)
        $bitmap = $null; $graphics = $null
        $context = $null; $contextGraphics = $null; $pen = $null
        try {
            $box = $task.Box
            $x = [int][Math]::Floor([double]$box.x * $image.Width)
            $y = [int][Math]::Floor([double]$box.y * $image.Height)
            $w = [Math]::Max(1, [int][Math]::Ceiling([double]$box.width * $image.Width))
            $h = [Math]::Max(1, [int][Math]::Ceiling([double]$box.height * $image.Height))
            $marginX = [int][Math]::Ceiling($w * .25)
            $marginY = [int][Math]::Ceiling($h * .25)
            $left = [Math]::Max(0, $x - $marginX)
            $top = [Math]::Max(0, $y - $marginY)
            $right = [Math]::Min($image.Width, $x + $w + $marginX)
            $bottom = [Math]::Min($image.Height, $y + $h + $marginY)
            $cw = [Math]::Max(1, $right - $left)
            $ch = [Math]::Max(1, $bottom - $top)
            $scale = [Math]::Min(1.0, $CropLongEdge / [double][Math]::Max($cw,$ch))
            $outW = [Math]::Max(1, [int][Math]::Round($cw * $scale))
            $outH = [Math]::Max(1, [int][Math]::Round($ch * $scale))
            $bitmap = [Drawing.Bitmap]::new($outW, $outH)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($image,
                [Drawing.Rectangle]::new(0,0,$outW,$outH),
                [Drawing.Rectangle]::new($left,$top,$cw,$ch),
                [Drawing.GraphicsUnit]::Pixel)
            $bitmap.Save((Join-Path $output ('crops\{0}.png' -f $task.Id)),
                [Drawing.Imaging.ImageFormat]::Png)

            $ratio = [Math]::Min(1.0, 720.0/[double][Math]::Max($image.Width,$image.Height))
            $scaledW = [Math]::Max(1,[int][Math]::Round($image.Width * $ratio))
            $scaledH = [Math]::Max(1,[int][Math]::Round($image.Height * $ratio))
            $context = [Drawing.Bitmap]::new($scaledW,$scaledH)
            $contextGraphics = [Drawing.Graphics]::FromImage($context)
            $contextGraphics.DrawImage($image,0,0,$scaledW,$scaledH)
            $pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255,220,90),3)
            $contextGraphics.DrawRectangle($pen,
                [Math]::Max(0,[int][Math]::Floor([double]$box.x * $scaledW)),
                [Math]::Max(0,[int][Math]::Floor([double]$box.y * $scaledH)),
                [Math]::Max(1,[int][Math]::Ceiling([double]$box.width * $scaledW)),
                [Math]::Max(1,[int][Math]::Ceiling([double]$box.height * $scaledH)))
            $context.Save((Join-Path $output ('context\{0}.jpg' -f $task.Id)),
                [Drawing.Imaging.ImageFormat]::Jpeg)
        } finally {
            if ($null -ne $pen) {$pen.Dispose()}
            if ($null -ne $contextGraphics) {$contextGraphics.Dispose()}
            if ($null -ne $context) {$context.Dispose()}
            if ($null -ne $graphics) {$graphics.Dispose()}
            if ($null -ne $bitmap) {$bitmap.Dispose()}
            $image.Dispose()
        }

        $gallery.Add(('<article data-sample="{0}" data-frame="{1}" data-face="{2}"><h2>{0}</h2>' +
            '<div class="sample-meta">Temporal section {3}/12 · detector confidence {4:F3} · crop-quality proxy {5:F3} (not face probabilities)</div>' +
            '<div class="images"><a href="crops/{0}.png" target="_blank"><img alt="Cropped candidate" src="crops/{0}.png"></a>' +
            '<a href="context/{0}.jpg" target="_blank"><img alt="Source context with candidate box" src="context/{0}.jpg"></a></div>' +
            '<label>Human label <select class="audit-label"><option value="">Not reviewed</option>' +
            '<option value="Real face">Real face</option><option value="Not a face">Not a face</option>' +
            '<option value="Cannot tell">Cannot tell</option></select></label>' +
            '<label>Person alias (optional, only for Real face) <input class="audit-person" maxlength="7" placeholder="P0001 or UNKNOWN"></label></article>' -f
            (Esc $task.Id),$rows[[int]($task.Id.Substring(1))-1].Frame,
            $rows[[int]($task.Id.Substring(1))-1].Face,$task.Stratum,
            $task.Confidence,$task.Quality))
    }
    $gallery[0] = $gallery[0].Replace('CLIP_ALIAS_TOKEN', (Esc $ClipAlias))
    $gallery.Add('</div><script src="unassigned-review.js"></script></main></body></html>')

    $worksheet = Join-Path $output 'unassigned-review.csv'
    $rows | Export-Csv -LiteralPath $worksheet -NoTypeInformation -Encoding utf8
    $js = LocalFile (Join-Path $PSScriptRoot 'VideoFaceUnassignedReview.js') (64KB)
    Copy-Item -LiteralPath $js -Destination (Join-Path $output 'unassigned-review.js')
    $htmlFile = Join-Path $output 'unassigned-review.html'
    $gallery | Set-Content -LiteralPath $htmlFile -Encoding utf8
}
catch {
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Force -Recurse -ErrorAction SilentlyContinue
    }
    throw
}
if ($OpenGallery) { Invoke-Item -LiteralPath $htmlFile }
# Only aggregate counts and private file paths in the LOCAL operator console;
# never emit identities or detailed ranked evidence to CI logs.
[pscustomobject]@{
    SchemaVersion = 1
    ClipAlias = $ClipAlias
    GalleryPath = $htmlFile
    WorksheetPath = $worksheet
    SampledCrops = $rows.Count
    TotalUnassignedObservations = [int]$audit.totalUnassignedObservations
    UnrepresentedObservations = [int]$audit.totalUnassignedObservations - $rows.Count
    TemporalStrataRepresented = $strata.Count
}
