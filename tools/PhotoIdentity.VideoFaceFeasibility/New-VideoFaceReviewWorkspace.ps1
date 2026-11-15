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
    $folder = Split-Path -Parent $Audit.GalleryPath
    $galleryLink = RelativeLocal $GroupFolder $Audit.GalleryPath
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
        $cropLink = RelativeLocal $GroupFolder $crop
        $tile = ('<a class="evidence-tile" href="{0}#sample-{1}" target="_blank" rel="noopener"><img loading="lazy" src="{2}" alt="Unassigned review crop {1}"><span>{1} · open to label</span></a>' -f
            (Html $galleryLink),(Html $sample),(Html $cropLink))
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
    return ('<section id="lane-{0}" class="evidence-lane"><details><summary><strong>{1}</strong> · {2} sampled of {3} observations · {4} not represented</summary><p class="note">These are detection observations, NOT separate people. Sampling is quality-biased. Open a crop to label it in its dedicated source-checked gallery and save that separate CSV. Unrepresented observations remain counted; never assume they are background.</p><p><a href="{5}" target="_blank" rel="noopener">Open full {6} gallery and save reviewed CSV</a></p><div class="evidence-tiles">{7}</div>{8}</details></section>' -f
        (Html $SectionId),(Html $Title),$count,
        [int]$Audit.TotalUnassignedObservations,
        [int]$Audit.UnrepresentedObservations,
        (Html $galleryLink),(Html $Prefix),
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
.evidence-lane{margin-top:22px;border:1px solid #526c82;border-radius:8px;padding:12px;background:#172a3b}
.evidence-lane summary{cursor:pointer;padding:8px;font-size:15px;color:#e9f5ff}
.evidence-lane a{color:#acd9ff}
.evidence-tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(125px,1fr));gap:10px;margin:12px 0}
.evidence-tile{display:flex;flex-direction:column;align-items:center;justify-content:space-between;padding:8px;border:1px solid #455d71;border-radius:5px;text-decoration:none;background:#101c2a}
.evidence-tile img{width:100%;height:110px;object-fit:contain}
.evidence-tile span{font-size:12px;margin-top:6px}
.evidence-extra{border-top:1px solid #526c82;padding:8px 0}
'@
$intro = ('<p class="note"><strong>Group-first private review · {0}.</strong> Review the {1} provisional C-groups first, then expand either evidence lane below for possible faces outside groups. Each source has its own private CSV; nothing here automatically merges groups, rejects faces or writes identities.</p><p><a href="#lane-noise">Ungrouped corroborated evidence</a> · <a href="#lane-uncorroborated">Detector disagreements</a></p>' -f
    (Html $ClipAlias),[int]$groups.Groups)
$page = $page.Replace($styleAnchor,($css + $styleAnchor))
$page = $page.Replace($gridAnchor,($intro + $gridAnchor))
$page = $page.Replace($endAnchor,(
    $noiseLane + [Environment]::NewLine + $uncorroboratedLane +
    [Environment]::NewLine + $endAnchor))
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
