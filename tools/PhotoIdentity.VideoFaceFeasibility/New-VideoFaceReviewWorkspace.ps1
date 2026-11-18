<#
.SYNOPSIS
Build a PRIVATE, group-first WI-0155 video review workspace.
.DESCRIPTION
Reuses source-validated C-group, N-noise and U-disagreement galleries.
The group page gains two bounded/expandable evidence lanes with links to
the exact audit sample for labeling. Separate source CSVs remain intact.
No database, inference, video access, automatic filtering or identity writes.
Requires PowerShell 7.6 on Windows and already-approved local frames.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PrivateReportPath,
    [Parameter(Mandatory)][string]$PrivateCasesPath,
    [ValidatePattern('^V(?!00)[0-9]{2}$')][string]$ClipAlias = 'V03',
    [ValidateRange(240,640)][int]$CropLongEdge = 512,
    [ValidateRange(2,12)][int]$SpotlightPerLane = 8,
    [switch]$OpenGallery
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or -not $env:LOCALAPPDATA) {
    throw 'Private group-first review requires Windows PowerShell 7.6.'
}
function Html([object]$Value) {
    return [Net.WebUtility]::HtmlEncode([string]$Value)
}
function RelativeLocal([string]$FromFolder,[string]$ToFile) {
    # Both paths come from freshly built, source-validated private galleries.
    $value = [IO.Path]::GetRelativePath($FromFolder,$ToFile).Replace('\','/')
    if ([IO.Path]::IsPathRooted($value) -or
        $value -notmatch '^[A-Za-z0-9_./-]+$') {
        throw 'Unsafe local gallery reference.'
    }
    return $value
}
function EvidenceLane(
    [object]$Audit,[string]$Title,[string]$SectionId,[string]$Prefix,
    [string]$GroupFolder,[string]$Clip,[string]$ReportHash,[int]$Spotlight
) {
    $rows = @(Import-Csv -LiteralPath $Audit.WorksheetPath -Encoding utf8)
    $count = $rows.Count
    if ($count -lt 1 -or $count -gt 96 -or
        $count -ne [int]$Audit.SampledCrops -or
        $count + [int]$Audit.UnrepresentedObservations -ne
          [int]$Audit.TotalUnassignedObservations) {
        throw 'Audit candidate and unrepresented counts did not reconcile.'
    }
    $source = if ($Prefix -ceq 'N') { 'CenterFaceNoise' }
        elseif ($Prefix -ceq 'U') { 'CenterFaceUncorroborated' }
        else { throw 'Unexpected private audit source.' }
    if ($Audit.AuditSource -cne $source) {
        throw 'Private audit source does not match its preview lane.'
    }
    $folder = Split-Path -Parent $Audit.GalleryPath
    $galleryLink = RelativeLocal $GroupFolder $Audit.GalleryPath
    # Copy only generated private thumbnails into the entry-point folder.
    # Chromium file:// origins and a self-only image CSP can block images
    # linked directly from a sibling gallery's directory.
    $previewFolder = Join-Path $GroupFolder 'audit-previews'
    New-Item -ItemType Directory -Path $previewFolder -Force | Out-Null
    if (((Get-Item -LiteralPath $previewFolder -Force).Attributes -band
         [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Private thumbnail folder cannot be a link.'
    }
    $selected = [Collections.Generic.HashSet[int]]::new()
    $firstCount = [Math]::Min($count,$Spotlight)
    for ($i=0; $i -lt $firstCount; $i++) {
        $index = if ($firstCount -eq 1) { 0 } else {
            [int][Math]::Floor(($i * ($count - 1.0)) / ($firstCount - 1))
        }
        if (-not $selected.Add($index)) {
            throw 'Duplicate temporal spotlight index.'
        }
    }
    $first = [Collections.Generic.List[string]]::new()
    $remaining = [Collections.Generic.List[string]]::new()
    for ($i=0; $i -lt $count; $i++) {
        $row = $rows[$i]
        $sample = [string]$row.Sample
        if ($sample -cne ('{0}{1:D3}' -f $Prefix,($i + 1)) -or
            [string]$row.Clip -cne $Clip -or
            [string]$row.AuditReportSha256 -cne $ReportHash -or
            [int]$row.Frame -lt 1 -or [int]$row.Face -lt 1 -or
            [string]$row.Label -cne '' -or
            [string]$row.PersonAlias -cne '') {
            throw 'Audit worksheet does not match this clip and report.'
        }
        $crop = Join-Path $folder ('crops\{0}.png' -f $sample)
        if (-not (Test-Path -LiteralPath $crop -PathType Leaf)) {
            throw 'Expected private review thumbnail is missing.'
        }
        $destination = Join-Path $previewFolder ('{0}.png' -f $sample)
        Copy-Item -LiteralPath $crop -Destination $destination -ErrorAction Stop
        $cropLink = 'audit-previews/{0}.png' -f $sample
        $tile = ('<article class="ws-audit-card" data-source="{0}" data-clip="{1}" data-report="{2}" data-sample="{3}" data-frame="{4}" data-face="{5}"><a class="evidence-tile" href="{6}#sample-{3}" target="_blank" rel="noopener"><img loading="lazy" src="{7}" alt="Review crop {3}"><span>{3} · context / enlarged crop</span></a><label>Face classification <select class="ws-label"><option value="">Not reviewed</option><option>Real face</option><option>Not a face</option><option>Cannot tell</option></select></label><label>Person alias (optional) <input class="ws-person" maxlength="7" placeholder="P0001 or UNKNOWN" disabled></label></article>' -f
            (Html $source),(Html $Clip),(Html $ReportHash),(Html $sample),
            (Html $row.Frame),(Html $row.Face),
            (Html $galleryLink),(Html $cropLink))
        if ($selected.Contains($i)) {
            $first.Add($tile)
        } else {
            $remaining.Add($tile)
        }
    }
    $extra = ''
    if ($remaining.Count -gt 0) {
        $extra = ('<details class="evidence-extra"><summary>Show {0} more sampled crops</summary><div class="evidence-tiles">{1}</div></details>' -f
            $remaining.Count,($remaining -join [Environment]::NewLine))
    }
    return ('<section id="lane-{0}" class="evidence-lane"><details><summary><strong>{1}</strong> · {2} sampled of {3} observations · {4} not represented</summary><p class="note">These observations are NOT separate people and sampling is quality-biased. Classify directly below each crop, and optionally resolve a private person alias. Open the linked context image if uncertain. Save all C/N/U decisions together with the workspace button. Unrepresented observations remain counted; never assume they are background.</p><div class="evidence-tiles">{5}</div>{6}</details></section>' -f
        (Html $SectionId),(Html $Title),$count,
        [int]$Audit.TotalUnassignedObservations,
        [int]$Audit.UnrepresentedObservations,
        ($first -join [Environment]::NewLine),$extra)
}
$groupCmd = Join-Path $PSScriptRoot 'New-VideoFaceGroupReview.ps1'
$auditCmd = Join-Path $PSScriptRoot 'New-VideoFaceUnassignedReview.ps1'
$common = @{
    PrivateReportPath = $PrivateReportPath
    PrivateCasesPath = $PrivateCasesPath
    ClipAlias = $ClipAlias
    CropLongEdge = $CropLongEdge
}
# Existing scripts own all input geometry, source and report validation.
# Generate three galleries but open only the completed group-first entry point.
$groups = & $groupCmd @common -GroupSource CenterFaceShadow
$noise = & $auditCmd @common -AuditSource CenterFaceNoise
$uncorroborated = & $auditCmd @common -AuditSource CenterFaceUncorroborated
if ($groups.GroupSource -cne 'CenterFaceShadow' -or
    $noise.AuditSource -cne 'CenterFaceNoise' -or
    $uncorroborated.AuditSource -cne 'CenterFaceUncorroborated' -or
    $noise.ClipAlias -cne $ClipAlias -or
    $uncorroborated.ClipAlias -cne $ClipAlias -or
    [int]$groups.GroupsNotRepresented -ne 0 -or
    [int]$groups.NoiseObservations -ne [int]$noise.TotalUnassignedObservations -or
    [int]$groups.NonCorroboratedObservations -ne
      [int]$uncorroborated.TotalUnassignedObservations) {
    throw 'CenterFace C/N/U source counts do not reconcile.'
}
$groupRows = @(Import-Csv -LiteralPath $groups.WorksheetPath -Encoding utf8)
if ($groupRows.Count -ne [int]$groups.Groups -or
    $groupRows.Count -lt 1 -or $groupRows.Count -gt 64) {
    throw 'Group-first worksheet is missing or incomplete.'
}
$uniqueGroups = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($row in $groupRows) {
    if ($row.Clip -cne $ClipAlias -or
        [string]$row.Group -cnotmatch '^C(?!000)[0-9]{3}$' -or
        -not $uniqueGroups.Add([string]$row.Group)) {
        throw 'Unexpected or duplicate group-first group identity.'
    }
}
$reportHash = (Get-FileHash -LiteralPath $PrivateReportPath -Algorithm SHA256).Hash.ToLowerInvariant()
$noiseLane = EvidenceLane $noise 'Corroborated, ungrouped candidates (N)' 'noise' 'N' $groups.ReviewFolder $ClipAlias $reportHash $SpotlightPerLane
$uncorroboratedLane = EvidenceLane $uncorroborated 'YuNet/CenterFace disagreements (U)' 'uncorroborated' 'U' $groups.ReviewFolder $ClipAlias $reportHash $SpotlightPerLane

$page = Get-Content -LiteralPath $groups.GalleryPath -Raw -Encoding utf8
$styleAnchor = '</style>'
$gridAnchor = '<div class="grid">'
$endAnchor = '</main><script src="group-review.js"></script>'
if (-not $page.Contains($styleAnchor) -or
    -not $page.Contains($gridAnchor) -or
    -not $page.Contains($endAnchor)) {
    throw 'Unexpected group gallery HTML structure.'
}
$css = @'
#save-groups,#import-groups{display:none}
.ws-toolbar{display:flex;align-items:center;gap:12px;flex-wrap:wrap;padding:14px;margin:10px 0;border:1px solid #59718a;border-radius:8px}
.ws-audit-card{border:1px solid #4c6378;border-radius:8px;background:#1a2c3d;padding:10px;min-width:0}
.ws-audit-card label{display:block;font-size:12px;margin-top:7px;color:#d2e5f3}
.ws-audit-card select,.ws-audit-card input{width:100%;box-sizing:border-box;margin-top:3px}
.ws-merge-assist{padding:12px 15px;border:1px solid #5a7590;border-radius:8px;margin:12px 0;background:#15293a}
.ws-merge-assist summary{cursor:pointer;font-size:17px;font-weight:600}
.ws-merge-assist p{color:#d5e3f0}
.ws-merge-pair{border:1px solid #4a6176;padding:10px;margin:9px 0;border-radius:8px;background:#192b3e}
.ws-merge-pictures{display:flex;gap:14px;flex-wrap:wrap}
.ws-merge-preview{flex:1 1 240px;min-width:0}
.ws-merge-images{display:flex;gap:5px;overflow-x:auto;margin:8px 0}
.ws-merge-images img{max-width:100px;max-height:110px;object-fit:contain}
.ws-merge-checkbox{display:block;margin:8px 0}
.ws-merge-checkbox input{width:auto}
.ws-merge-pair button:disabled{opacity:.5;cursor:not-allowed}
.ws-audit-card[hidden]{display:none}
.evidence-lane{margin-top:22px;border:1px solid #526c82;border-radius:8px;padding:12px;background:#172a3b}
.evidence-lane summary{cursor:pointer;padding:8px;font-size:15px;color:#e9f5ff}
.evidence-lane a{color:#acd9ff}
.evidence-tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(125px,1fr));gap:10px;margin:12px 0}
.evidence-tile{display:flex;flex-direction:column;align-items:center;justify-content:space-between;padding:8px;border:1px solid #455d71;border-radius:5px;text-decoration:none;background:#101c2a}
.evidence-tile img{width:100%;height:110px;object-fit:contain}
.evidence-tile span{font-size:12px;margin-top:6px}
.evidence-extra{border-top:1px solid #526c82;padding:8px 0}
'@
$intro = ('<div id="ws-toolbar" class="ws-toolbar" data-clip="{0}" data-report="{1}"><button type="button" id="ws-save">Save all C/N/U decisions (one CSV)</button><button type="button" id="ws-import">Import combined CSV</button><input type="file" id="ws-file" accept=".csv,text/csv" hidden><span id="ws-status" aria-live="polite">No decisions saved yet</span></div><p class="note"><strong>Private group-first review · {0}.</strong> Review the {2} C-groups first. Expand N and U below, classify their crops in this same page, then save everything using the single button above. Earlier individual galleries remain available for context, but their separate Save buttons are not needed. This is a private worksheet, not a production identity update.</p><p><a href="#lane-noise">Ungrouped corroborated evidence</a> · <a href="#lane-uncorroborated">Detector disagreements</a></p>' -f
    (Html $ClipAlias),(Html $reportHash),[int]$groups.Groups)
$mergePanel = @'
<section id="ws-merge-assist" class="ws-merge-assist">
  <details>
    <summary>Review possible same-person group fragments (human confirmation)</summary>
    <p class="note">This optional *pairwise* shortlist only uses shared top-three catalogue aliases and explicitly reviewed person aliases, NOT reliable pairwise video-face similarity. Shared ranks can be wrong. Compare crops before any merge; crop-frame overlap blocks a lead but absence of overlap among representative crops does not prove a merge is safe. No cross-person or automatic identity assignment.</p>
    <p id="ws-merge-summary" aria-live="polite"></p>
    <div id="ws-merge-proposals"></div>
  </details>
</section>
'@
$page = $page.Replace($styleAnchor,($css + $styleAnchor))
$page = $page.Replace($gridAnchor,($intro + $mergePanel + $gridAnchor))
$page = $page.Replace($endAnchor,(
    $noiseLane + [Environment]::NewLine + $uncorroboratedLane +
    [Environment]::NewLine + $endAnchor +
    '<script src="review-workspace.js"></script>' +
    '<script src="merge-assist.js"></script>'))
$workspaceJs = Join-Path $PSScriptRoot 'VideoFaceReviewWorkspace.js'
if (-not (Test-Path -LiteralPath $workspaceJs -PathType Leaf)) {
    throw 'Private combined review script is missing.'
}
Copy-Item -LiteralPath $workspaceJs -Destination (
    Join-Path $groups.ReviewFolder 'review-workspace.js')
$mergeAssistJs = Join-Path $PSScriptRoot 'VideoFaceGroupMergeAssist.js'
if (-not (Test-Path -LiteralPath $mergeAssistJs -PathType Leaf)) {
    throw 'Private group-merge assistant script is missing.'
}
Copy-Item -LiteralPath $mergeAssistJs -Destination (
    Join-Path $groups.ReviewFolder 'merge-assist.js')
$page | Set-Content -LiteralPath $groups.GalleryPath -Encoding utf8

if ($OpenGallery) { Invoke-Item -LiteralPath $groups.GalleryPath }
# Operator output contains only aggregate counts and private local paths.
[pscustomobject]@{
    SchemaVersion = 1
    ClipAlias = $ClipAlias
    GroupSource = 'CenterFaceShadow'
    GalleryPath = $groups.GalleryPath
    GroupWorksheetPath = $groups.WorksheetPath
    NoiseGalleryPath = $noise.GalleryPath
    NoiseWorksheetPath = $noise.WorksheetPath
    UncorroboratedGalleryPath = $uncorroborated.GalleryPath
    UncorroboratedWorksheetPath = $uncorroborated.WorksheetPath
    Groups = [int]$groups.Groups
    GroupCrops = [int]$groups.Crops
    NoiseSamples = [int]$noise.SampledCrops
    NoiseNotRepresented = [int]$noise.UnrepresentedObservations
    UncorroboratedSamples = [int]$uncorroborated.SampledCrops
    UncorroboratedNotRepresented = [int]$uncorroborated.UnrepresentedObservations
    SpotlightPerLane = $SpotlightPerLane
}
