<#
.SYNOPSIS
Create a small, OFFLINE, PRIVATE visual shortlist from one WI-0155 catalogue
report and the original approved frame manifests. PowerShell 7.6 on Windows.
No inference, database, network, original video access or catalogue writes.
.EXAMPLE
.\New-VideoFaceReviewGallery.ps1 -PrivateReportPath $boundedReport -PrivateCasesPath $newCasesPath -OpenGallery
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PrivateReportPath,
    [Parameter(Mandatory)][string] $PrivateCasesPath,
    [ValidateRange(1, 3)][int] $FramesPerPerson = 2,
    [ValidateRange(1, 6)][int] $UncertainFramesPerClip = 3,
    [ValidateRange(1, 6)][int] $OverflowFramesPerClip = 4,
    [ValidateRange(240, 960)][int] $ThumbnailLongEdge = 600,
    [string] $GeometryPath,
    [switch] $OpenGallery
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    throw 'LOCALAPPDATA is required for a local non-synced review folder.'
}

function Assert-PrivateFile {
    param([string] $Path, [long] $MaximumBytes)
    $full = [IO.Path]::GetFullPath($Path)
    $file = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    $blocked = [long][IO.FileAttributes]::ReparsePoint -bor
        [long][IO.FileAttributes]::Offline -bor
        [long]0x00040000 -bor [long]0x00400000
    if ($file.PSIsContainer -or $file.Length -lt 1 -or
        $file.Length -gt $MaximumBytes -or
        ([long]$file.Attributes -band $blocked) -ne 0) {
        throw 'Input must be a fully local, bounded, regular file.'
    }
    $folder = Get-Item -LiteralPath (Split-Path -Parent $full)
    if (($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Linked input directories are not permitted.'
    }
    return $full
}

function To-Html {
    param([object] $Value)
    return [Net.WebUtility]::HtmlEncode([string]$Value)
}

$reportFile = Assert-PrivateFile $PrivateReportPath (64MB)
$casesFile = Assert-PrivateFile $PrivateCasesPath (16KB)
$report = Get-Content -LiteralPath $reportFile -Raw -Encoding utf8 | ConvertFrom-Json
$cases = Get-Content -LiteralPath $casesFile -Raw -Encoding utf8 | ConvertFrom-Json
$caseClips = @($cases.clips)
$reportClips = @($report.clips)

if ($report.schemaVersion -ne 1 -or $cases.schemaVersion -ne 1 -or
    $caseClips.Count -lt 2 -or $caseClips.Count -gt 4 -or
    $reportClips.Count -ne $caseClips.Count -or
    @($cases.people).Count -lt 1 -or @($cases.people).Count -gt 32) {
    throw 'Unsupported or inconsistent private report/case selection.'
}

$personAliases = @($cases.people | ForEach-Object { [string]$_.alias } | Sort-Object -Unique)
if ($personAliases.Count -ne @($cases.people).Count -or
    @($personAliases | Where-Object { $_ -cnotmatch '^P(?!00)[0-9]{2}$' }).Count -gt 0) {
    throw 'Invalid person pseudonyms in private cases.'
}

$entries = [Collections.Generic.List[object]]::new()
$manifestFrames = @{}
$clipSummaries = [Collections.Generic.List[object]]::new()

function Add-ReviewEntry {
    param(
        [string] $Clip, [string] $Section, [int] $Frame,
        [object] $Face, [string] $Person, [object] $Score,
        [string] $Description
    )
    $selectedFrames = $manifestFrames[$Clip]
    if ($Frame -lt 1 -or $Frame -gt $selectedFrames.Count) {
        throw 'A report refers to a frame outside its approved manifest.'
    }
    $fileName = [string]$selectedFrames[$Frame - 1].file
    if ($fileName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\.(png|jpg|jpeg)$') {
        throw 'Invalid approved frame filename.'
    }

    $entries.Add([pscustomobject]@{
        Clip = $Clip
        Section = $Section
        Frame = $Frame
        Face = $Face
        Candidate = $Person
        Score = $Score
        Note = $Description
        OriginalPath = $selectedFrames[$Frame - 1].fullPath
        Thumbnail = ''
        Crop = ''
        Overlay = ''
    })
}

foreach ($clip in $reportClips) {
    $alias = [string]$clip.alias
    if ($alias -cnotmatch '^V(?!00)[0-9]{2}$' -or
        @($clipSummaries | Where-Object Clip -CEQ $alias).Count -gt 0) {
        throw 'Invalid or duplicated private video pseudonym.'
    }
    $matchedCases = @($caseClips | Where-Object { $_.alias -ceq $alias })
    if ($matchedCases.Count -ne 1) {
        throw 'A report video does not match the approved case file.'
    }

    $manifestFile = Assert-PrivateFile ([string]$matchedCases[0].manifest) (16KB)
    $manifest = Get-Content -LiteralPath $manifestFile -Raw -Encoding utf8 | ConvertFrom-Json
    $frames = @($manifest.frames)
    if ($manifest.schemaVersion -ne 1 -or
        $frames.Count -lt 1 -or $frames.Count -gt 180 -or
        $frames.Count -ne [int]$clip.review.aggregate.framesExamined) {
        throw 'An approved frame manifest does not match the report.'
    }
    $frameFolder = Split-Path -Parent $manifestFile
    foreach ($frame in $frames) {
        $name = [string]$frame.file
        if ($name -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\.(png|jpg|jpeg)$') {
            throw 'Invalid approved frame filename.'
        }
        # Reading the selected frame is deferred until thumbnail generation.
        $frame | Add-Member -NotePropertyName fullPath -NotePropertyValue (
            Join-Path $frameFolder $name
        ) -Force
    }
    $manifestFrames[$alias] = $frames

    $diagnostics = $clip.review.diagnostics
    if ($null -eq $diagnostics) {
        throw 'Visual review requires the explicit private diagnostics report.'
    }
    $frameDetails = @($diagnostics.frameDetections)
    $ranked = @($diagnostics.rankedFaces)
    $findings = @($clip.review.findings)
    if ($frameDetails.Count -ne $frames.Count -or
        $ranked.Count -ne [int]$clip.review.aggregate.facesDetected -or
        $findings.Count -ne $ranked.Count) {
        throw 'Private diagnostic counts are inconsistent.'
    }

    $rawTotal = 0
    $discardedTotal = 0
    foreach ($item in $frameDetails) {
        $raw = if ($null -ne $item.rawCandidatesDetected) {
            [int]$item.rawCandidatesDetected
        } else { [int]$item.facesDetected }
        $discarded = if ($null -ne $item.candidatesDiscarded) {
            [int]$item.candidatesDiscarded
        } else { 0 }
        if ($raw -ne ([int]$item.facesDetected + $discarded)) {
            throw 'Invalid raw/retained/discarded candidate accounting.'
        }
        $rawTotal += $raw
        $discardedTotal += $discarded
    }
    $clipSummaries.Add([pscustomobject]@{
        Clip = $alias
        Frames = $frames.Count
        Retained = [int]$clip.review.aggregate.facesDetected
        Raw = $rawTotal
        Discarded = $discardedTotal
        OverflowFrames = @($frameDetails | Where-Object { $_.candidatesDiscarded -gt 0 }).Count
    })

    $findingStatuses = @{}
    foreach ($finding in $findings) {
        $key = '{0}:{1}' -f [int]$finding.frameNumber, [int]$finding.faceNumber
        $findingStatuses[$key] = [string]$finding.reviewStatus
    }

    # Review the strongest *ranked observations* for each selected person.
    # They are NOT verified identifications and can have very low similarity.
    foreach ($person in $personAliases) {
        $choices = @($ranked |
            Where-Object { $_.topReferenceAlias -ceq $person } |
            Sort-Object @{ Expression = { [double]$_.topSimilarity }; Descending = $true },
                        @{ Expression = { [int]$_.frameNumber }; Descending = $false })
        $used = [Collections.Generic.HashSet[int]]::new()
        foreach ($candidate in $choices) {
            $frameNo = [int]$candidate.frameNumber
            if (-not $used.Add($frameNo)) { continue }
            $key = '{0}:{1}' -f $frameNo, [int]$candidate.faceNumber
            $status = if ($findingStatuses.ContainsKey($key)) {
                $findingStatuses[$key]
            } else { 'unclassified' }
            Add-ReviewEntry $alias 'Top-ranked by person' $frameNo (
                [int]$candidate.faceNumber) $person (
                [double]$candidate.topSimilarity) $status
            if ($used.Count -ge $FramesPerPerson) { break }
        }
    }

    # Sample uncertain/weak evidence for human review. 'Below threshold'
    # does not establish that a person is unknown.
    $uncertain = @($findings |
        Where-Object { $_.reviewStatus -ne 'review-candidate' } |
        Sort-Object @{ Expression = { [double]$_.similarity }; Descending = $true },
                    @{ Expression = { [int]$_.frameNumber }; Descending = $false })
    $used = [Collections.Generic.HashSet[int]]::new()
    foreach ($item in $uncertain) {
        $frameNo = [int]$item.frameNumber
        if (-not $used.Add($frameNo)) { continue }
        Add-ReviewEntry $alias 'Uncertain suggestions' $frameNo (
            [int]$item.faceNumber) '' ([double]$item.similarity) (
            [string]$item.reviewStatus)
        if ($used.Count -ge $UncertainFramesPerClip) { break }
    }

    # Explicitly surface loss-of-evidence risk from top-32 selection.
    $overflow = @($frameDetails |
        Where-Object { $_.candidatesDiscarded -gt 0 } |
        Sort-Object @{ Expression = { [int]$_.candidatesDiscarded }; Descending = $true },
                    @{ Expression = { [int]$_.frameNumber }; Descending = $false } |
        Select-Object -First $OverflowFramesPerClip)
    foreach ($item in $overflow) {
        $detail = 'raw {0}; retained {1}; dropped {2}' -f (
            [int]$item.rawCandidatesDetected), (
            [int]$item.facesDetected), (
            [int]$item.candidatesDiscarded)
        Add-ReviewEntry $alias 'Overflow / omissions' ([int]$item.frameNumber) $null '' $null $detail
    }
}

if ($entries.Count -eq 0) {
    throw 'No reviewable observations found. Preserve the underlying private report.'
}

$selectedKeys = @($entries |
    ForEach-Object { '{0}:{1}' -f $_.Clip, $_.Frame } |
    Sort-Object -Unique)
if ($selectedKeys.Count -gt 128) {
    throw 'Review shortlist exceeds the bounded reconstruction budget.'
}

# Optional geometry comes from explicitly re-running only these selected frames,
# with the same detector settings. Never trust box coordinates or face ordinals
# blindly when loading a separate local geometry file.
$geometryByFrame = @{}
if (-not [string]::IsNullOrWhiteSpace($GeometryPath)) {
    $geometryFile = Assert-PrivateFile $GeometryPath (1MB)
    $geometry = Get-Content -LiteralPath $geometryFile -Raw -Encoding utf8 |
        ConvertFrom-Json
    if ($geometry.schemaVersion -ne 1 -or
        @($geometry.frames).Count -ne $selectedKeys.Count) {
        throw 'Geometry does not match the selected frames.'
    }
    foreach ($item in @($geometry.frames)) {
        $key = '{0}:{1}' -f $item.clip,[int]$item.frameNumber
        if ($key -cnotin $selectedKeys -or $geometryByFrame.ContainsKey($key) -or
            @($item.boxes).Count -gt 32) {
            throw 'Geometry contains an unexpected or repeated frame.'
        }
        $faceNumbers = [Collections.Generic.HashSet[int]]::new()
        foreach ($box in @($item.boxes)) {
            $x = [double]$box.x
            $y = [double]$box.y
            $w = [double]$box.width
            $h = [double]$box.height
            if (-not $faceNumbers.Add([int]$box.faceNumber) -or
                [int]$box.faceNumber -lt 1 -or
                [int]$box.faceNumber -gt 32 -or
                -not ([double]::IsFinite($x) -and [double]::IsFinite($y) -and
                    [double]::IsFinite($w) -and [double]::IsFinite($h)) -or
                $x -lt 0 -or $y -lt 0 -or $w -le 0 -or $h -le 0 -or
                $x + $w -gt 1.000001 -or $y + $h -gt 1.000001) {
                throw 'Invalid normalized private face geometry.'
            }
        }
        $geometryByFrame[$key] = @($item.boxes)
    }
    foreach ($entry in $entries) {
        if ($null -ne $entry.Face -and
            @($geometryByFrame[('{0}:{1}' -f $entry.Clip,$entry.Frame)] |
                Where-Object { $_.faceNumber -eq [int]$entry.Face }).Count -ne 1) {
            throw 'A shortlisted face ordinal has no matching reconstructed box.'
        }
    }
}

# New private output. Deliberately outside the repository and extraction folders.
$outputRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\WI0155\review-galleries'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (((Get-Item -LiteralPath $outputRoot).Attributes -band
        [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Private output root must not be a linked directory.'
}
$outputFolder = Join-Path $outputRoot (
    'review-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $outputFolder -ErrorAction Stop | Out-Null
$thumbFolder = Join-Path $outputFolder 'thumbnails'
New-Item -ItemType Directory -Path $thumbFolder -ErrorAction Stop | Out-Null

$selectionFile = Join-Path $outputFolder 'review-selection.json'
$selectedFrames = @(
    foreach ($key in $selectedKeys) {
        $parts = $key.Split(':')
        [ordered]@{ clip = $parts[0]; frameNumber = [int]$parts[1] }
    }
)
@{ schemaVersion = 1; frames = $selectedFrames } |
    ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $selectionFile -Encoding utf8

$scriptAsset = Join-Path $PSScriptRoot 'VideoFaceReviewGallery.js'
[void](Assert-PrivateFile $scriptAsset (64KB))
Copy-Item -LiteralPath $scriptAsset -Destination (
    Join-Path $outputFolder 'review-ui.js') -ErrorAction Stop

if ($geometryByFrame.Count -gt 0) {
    $cropFolder = Join-Path $outputFolder 'crops'
    $overlayFolder = Join-Path $outputFolder 'overlays'
    New-Item -ItemType Directory -Path $cropFolder,$overlayFolder -ErrorAction Stop |
        Out-Null
}

# These are intentionally lossy, reduced-size PRIVATE review thumbnails;
# source frames and the experiment's image input remain untouched.
Add-Type -AssemblyName System.Drawing
$thumbnailCache = @{}
foreach ($entry in $entries) {
    $key = '{0}:{1}' -f $entry.Clip, $entry.Frame
    if (-not $thumbnailCache.ContainsKey($key)) {
        $source = Assert-PrivateFile $entry.OriginalPath (8MB)
        $thumbName = '{0}-frame-{1:D3}.jpg' -f $entry.Clip, $entry.Frame
        $destination = Join-Path $thumbFolder $thumbName
        $image = [Drawing.Image]::FromFile($source)
        $bitmap = $null
        $graphics = $null
        try {
            $ratio = [Math]::Min(1.0,
                $ThumbnailLongEdge / [double][Math]::Max($image.Width, $image.Height))
            $width = [Math]::Max(1, [int][Math]::Round($image.Width * $ratio))
            $height = [Math]::Max(1, [int][Math]::Round($image.Height * $ratio))
            $bitmap = [Drawing.Bitmap]::new($width, $height)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($image, 0, 0, $width, $height)
            $bitmap.Save($destination, [Drawing.Imaging.ImageFormat]::Jpeg)
        } finally {
            if ($null -ne $graphics) { $graphics.Dispose() }
            if ($null -ne $bitmap) { $bitmap.Dispose() }
            $image.Dispose()
        }
        $thumbnailCache[$key] = 'thumbnails/' + $thumbName
    }
    $entry.Thumbnail = $thumbnailCache[$key]
}

$html = [Collections.Generic.List[string]]::new()
$html.Add(@'
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'unsafe-inline'">
<title>WI-0155 private face-review shortlist</title>
<style>
body{background:#101923;color:#f0f3f7;font:15px/1.5 system-ui,Segoe UI,Arial,sans-serif;margin:0}
main{max-width:1380px;margin:auto;padding:28px 20px 55px}
h1{font-size:1.7rem;margin:0}h2{font-size:1.2rem}h3{font-size:1rem;margin:0 0 10px}
p{color:#c6d0da}strong{color:white}
.note{background:#243344;border-left:3px solid #8fc5f2;padding:12px 16px;border-radius:6px}
summary{cursor:pointer;font-size:1.15rem;font-weight:650;padding:12px;background:#253343;border-radius:7px}
details{margin:18px 0}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(225px,1fr));gap:14px}
article{background:#1d2936;border:1px solid #334656;border-radius:10px;overflow:hidden}
article img{display:block;width:100%;height:240px;object-fit:contain;background:#0b1219}
article .body{padding:11px 13px}
code{color:#aad2fa}a{color:#9fd4ff}small{color:#9fb3c5}
table{width:100%;border-collapse:collapse;margin:16px 0}
th,td{padding:8px 10px;text-align:left;border-bottom:1px solid #324354}
.tag{display:inline-block;border-radius:4px;background:#34475a;padding:2px 6px;font-size:12px}
</style>
</head>
<body><main>
<h1>WI-0155 · Private visual review</h1>
<p class="note"><strong>Important:</strong> Person aliases are exploratory cosine rankings, NOT identifications.
Under-threshold suggestions are NOT proof of unknown identity. Preview images are
downsized and may not show a small face well; use the full original frame for zooming.
This report cannot show individual face bounding boxes because those are not retained
in the current diagnostic format. Do not upload this HTML, CSV or its thumbnails.</p>
'@)

$html.Add('<h2>Video overview</h2><table><thead><tr><th>Video</th><th>Frames</th><th>Raw candidates</th><th>Retained</th><th>Discarded</th><th>Overflow frames</th></tr></thead><tbody>')
foreach ($summary in $clipSummaries) {
    $html.Add(('<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td></tr>' -f
        (To-Html $summary.Clip),$summary.Frames,$summary.Raw,
        $summary.Retained,$summary.Discarded,$summary.OverflowFrames))
}
$html.Add('</tbody></table><p>Only a few representative observations per selected person, uncertain cases and overflow frames appear below. Repeated observations are not independent identification successes.</p>')

foreach ($summary in $clipSummaries) {
    $clip = $summary.Clip
    $html.Add(('<details open><summary>{0} · {1} frames · {2} retained candidates</summary>' -f
        (To-Html $clip), $summary.Frames, $summary.Retained))
    $matchingReport = @($reportClips | Where-Object { $_.alias -ceq $clip })[0]
    $html.Add('<h3>Catalogue ranking coverage (not verified identification)</h3><table><thead><tr><th>Person alias</th><th>Top-ranked observations</th><th>Highest cosine</th><th>Strong review suggestions</th></tr></thead><tbody>')
    foreach ($person in $personAliases) {
        $personSummary = @($matchingReport.personRankSummaries |
            Where-Object { $_.alias -ceq $person })
        if ($personSummary.Count -eq 0) {
            $html.Add(('<tr><td>{0}</td><td>0</td><td>–</td><td>0</td></tr>' -f (To-Html $person)))
        } else {
            $score = ([double]$personSummary[0].bestTopRankSimilarity).ToString(
                'F4', [Globalization.CultureInfo]::InvariantCulture)
            $html.Add(('<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td></tr>' -f
                (To-Html $person),[int]$personSummary[0].topRankObservations,
                (To-Html $score),[int]$personSummary[0].strongReviewCandidates))
        }
    }
    $html.Add('</tbody></table>')
    foreach ($section in @('Top-ranked by person','Uncertain suggestions','Overflow / omissions')) {
        $cards = @($entries | Where-Object { $_.Clip -ceq $clip -and $_.Section -ceq $section })
        if ($cards.Count -eq 0) { continue }
        $html.Add(('<h3>{0} ({1} observations)</h3><div class="grid">' -f
            (To-Html $section),$cards.Count))
        foreach ($entry in $cards) {
            $sourceUri = ([Uri]::new([IO.Path]::GetFullPath($entry.OriginalPath))).AbsoluteUri
            $scoreLabel = if ($null -eq $entry.Score) { '–' } else {
                ([double]$entry.Score).ToString('F4',[Globalization.CultureInfo]::InvariantCulture)
            }
            $faceLabel = if ($null -eq $entry.Face) { 'not available' } else { [string]$entry.Face }
            $personLabel = if ([string]::IsNullOrWhiteSpace($entry.Candidate)) {
                'No verified person'
            } else { 'Ranked: ' + $entry.Candidate }
            $html.Add(('<article><img loading="lazy" src="{0}" alt="Private full-frame preview"><div class="body"><strong>Frame {1}</strong> <small>· face ordinal {2}</small><p>{3}<br>Similarity: {4}<br><small>{5}</small></p><a href="{6}">Open original full-resolution frame</a></div></article>' -f
                (To-Html $entry.Thumbnail),$entry.Frame,(To-Html $faceLabel),
                (To-Html $personLabel),(To-Html $scoreLabel),
                (To-Html $entry.Note),(To-Html $sourceUri)))
        }
        $html.Add('</div>')
    }
    $html.Add('</details>')
}
$html.Add('<p class="note">Review one useful face observation per genuinely visible person. Check wrong suggestions, unknown people, repetitive observations, and whether overflow discarded useful evidence. Record your decisions in the accompanying CSV worksheet. No catalogue assignments are made.</p></main></body></html>')

$galleryPath = Join-Path $outputFolder 'review.html'
$worksheetPath = Join-Path $outputFolder 'review-worksheet.csv'
[IO.File]::WriteAllLines($galleryPath,$html,[Text.UTF8Encoding]::new($false))
$entries | Select-Object Clip,Section,Frame,Face,Candidate,Score,Note,
    @{ Name = 'HumanDecision'; Expression = { '' } },
    @{ Name = 'ReviewerNotes'; Expression = { '' } } |
    Export-Csv -LiteralPath $worksheetPath -NoTypeInformation -Encoding utf8

Write-Host ('Private review observations: {0}' -f $entries.Count)
Write-Host ('Selected frame previews: {0}' -f $thumbnailCache.Count)
Write-Host 'Only private, local files were created. Existing reports, media and databases were not modified.'
[pscustomobject]@{
    GalleryPath = $galleryPath
    WorksheetPath = $worksheetPath
    ReviewFolder = $outputFolder
    Observations = $entries.Count
    Previews = $thumbnailCache.Count
}
if ($OpenGallery) {
    Invoke-Item -LiteralPath $galleryPath
}
