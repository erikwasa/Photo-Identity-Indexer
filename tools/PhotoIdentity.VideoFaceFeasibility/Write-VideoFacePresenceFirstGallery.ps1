<#
.SYNOPSIS
Internal, opt-in gallery writer for already source-validated WI-0155 G crops.
Called ONLY after New-VideoFaceGroupReview.ps1 validates and reconstructs
private frame crops. No inference, original video access, SQL or identities.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClipAlias,
    [Parameter(Mandatory)][string]$PrivateReportPath,
    [Parameter(Mandatory)][string]$ReviewFolder,
    [Parameter(Mandatory)][object[]]$Rows,
    [Parameter(Mandatory)][object[]]$Crops,
    [Parameter(Mandatory)][hashtable]$Rankings,
    [string]$PrivatePeopleMapPath,
    [ValidateRange(0,5760)][int]$UnassignedObservations = 0,
    [switch]$OpenGallery
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function HtmlEncode([object]$Value) { [Net.WebUtility]::HtmlEncode([string]$Value) }
if ($ClipAlias -cnotmatch '^V(?!00)[0-9]{2}$' -or
    $Rows.Count -lt 1 -or $Rows.Count -gt 64 -or
    $Crops.Count -lt 1 -or $Crops.Count -gt 320 -or
    -not [IO.Path]::IsPathFullyQualified($ReviewFolder) -or
    -not [IO.Path]::IsPathFullyQualified($PrivateReportPath)) {
    throw 'Invalid validated presence-first gallery parameters.'
}
$reportHash=(Get-FileHash -LiteralPath $PrivateReportPath -Algorithm SHA256).Hash.ToLowerInvariant()
# Metadata is bound to the already-validated original catalogue report.
# The name/UUID map itself remains a separate local-only user import.
$sourceReport=Get-Content -LiteralPath $PrivateReportPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 16
$snapshot=[string]$sourceReport.catalogue.pgSnapshot
$peopleCount=[int]$sourceReport.catalogue.selectedPeople
if ([int]$sourceReport.schemaVersion -ne 1 -or
    $snapshot -cnotmatch '^[0-9]+:[0-9]+:(?:[0-9]+(?:,[0-9]+)*)?$' -or
    $peopleCount -lt 1 -or $peopleCount -gt 4096) {
    throw 'Original catalogue snapshot metadata unavailable for named review.'
}
# Legacy v2 packages still work. A supplied original snapshot map produces
# v3 alias -> immutable person GUID bindings. Names are deliberately omitted:
# the web app resolves current canonical names directly from PostgreSQL.
$reviewBindings = @()
if ($PrivatePeopleMapPath) {
    if (-not [IO.Path]::IsPathFullyQualified($PrivatePeopleMapPath)) {
        throw 'An absolute original people-map path is required.'
    }
    $sourceMap = Get-Item -LiteralPath $PrivatePeopleMapPath -Force
    $blocked = [long][IO.FileAttributes]::ReparsePoint -bor
        [long][IO.FileAttributes]::Offline -bor [long]0x00040000 -bor [long]0x00400000
    if ($sourceMap.PSIsContainer -or $sourceMap.Length -lt 1 -or $sourceMap.Length -gt 4MB -or
        ([long]$sourceMap.Attributes -band $blocked) -ne 0) {
        throw 'Original people map must be a real bounded local file.'
    }
    $map = Get-Content -LiteralPath $sourceMap.FullName -Raw -Encoding utf8 | ConvertFrom-Json -Depth 12
    if ([int]$map.schemaVersion -ne 1 -or
        [string]$map.pgSnapshot -cne $snapshot -or
        @($map.people).Count -ne $peopleCount) {
        throw 'Private people map does not match the original catalogue snapshot.'
    }
    $aliases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $reviewBindings = @($map.people | ForEach-Object {
        $alias = [string]$_.alias
        $id = [Guid]::Empty
        if ($alias -cnotmatch '^P(?!0000)[0-9]{4}
foreach($row in $Rows) {
    if ([string]$row.Clip -cne $ClipAlias -or
        [string]$row.Group -cnotmatch '^G(?!000)[0-9]{3}$' -or
        -not $rowsById.TryAdd([string]$row.Group,$row) -or
        [int]$row.Observations -lt 1) {
        throw 'Inconsistent validated provisional groups.'
    }
}
$records=[Collections.Generic.List[object]]::new()
$byGroup=@{}
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($crop in $Crops) {
    $group=[string]$crop.Group
    if (-not $rowsById.ContainsKey($group)) { throw 'Crop group not in source.' }
    $box=$crop.Box
    $frame=[int]$box.frameNumber
    $face=[int]$box.faceNumber
    $key=('{0}:{1}' -f $frame,$face)
    $path=[string]$crop.Relative
    if ($frame -lt 1 -or $frame -gt 180 -or
        $face -lt 1 -or $face -gt 32 -or
        -not $seen.Add($key) -or
        $path -cnotmatch ('^crops/'+[regex]::Escape($ClipAlias)+'-'+[regex]::Escape($group)+'-[0-9]{2}\.png$') -or
        -not (Test-Path -LiteralPath (Join-Path $ReviewFolder $path) -PathType Leaf)) {
        throw 'Incomplete, reused or missing validated representative crop.'
    }
    if (-not $byGroup.ContainsKey($group)) { $byGroup[$group]=0 }
    $byGroup[$group]++
    if ($byGroup[$group] -gt [int]$rowsById[$group].Observations) {
        throw 'More crops than source observations.'
    }
    $records.Add([pscustomobject][ordered]@{
        Clip=$ClipAlias; ReportSha256=$reportHash; Group=$group
        Frame=[string]$frame; Face=[string]$face
        Decision=''; PersonAlias=''; ExcludeFromGroup='False'; ReviewerNotes=''
        Relative=$path
    })
}
foreach($row in $Rows) {
    if (-not $byGroup.ContainsKey([string]$row.Group)) {
        throw 'A source group has no review crop.'
    }
}
$folder=Get-Item -LiteralPath $ReviewFolder -Force
if (-not $folder.PSIsContainer -or
    ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Private review destination is not a local directory.'
}
$worksheet=Join-Path $ReviewFolder 'presence-first-worksheet.csv'
$records | Select-Object Clip,ReportSha256,Group,Frame,Face,Decision,
    PersonAlias,ExcludeFromGroup,ReviewerNotes |
    Export-Csv -LiteralPath $worksheet -Encoding utf8
$js=Join-Path $PSScriptRoot 'VideoFacePresenceFirstReview.js'
if (-not (Test-Path -LiteralPath $js -PathType Leaf)) {
    throw 'Presence-first browser script missing.'
}
Copy-Item -LiteralPath $js -Destination (Join-Path $ReviewFolder 'presence-first.js')
$html=[Collections.Generic.List[string]]::new()
$html.Add(@'
<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; script-src 'self'">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>WI-0155 · Presence-first private review</title>
<style>
:root{color-scheme:dark}body{font:15px system-ui,Segoe UI,sans-serif;color:#eaf1f6;background:#111a24;margin:24px}
main{max-width:1260px;margin:auto}.note{line-height:1.55;color:#c1d2df;max-width:1000px}
button,select,input,textarea{font:inherit;background:#223446;color:#edf4fa;border:1px solid #718b9c;border-radius:4px;padding:7px}
button{cursor:pointer;background:#245d86}.toolbar{display:flex;gap:12px;flex-wrap:wrap;align-items:center}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(330px,1fr));gap:15px;margin-top:18px}
.presence-group{background:#1a2c3d;border:1px solid #5e7689;border-radius:7px;padding:16px}
.presence-group h2{margin:0 0 8px;font-size:18px}.presence-crop{border-top:1px solid #465e72;padding:10px 0;display:grid;gap:6px}
.presence-crop img{max-height:190px;max-width:100%;object-fit:contain;background:#080e15}
label{display:grid;gap:5px}textarea{min-height:40px;resize:vertical}.rank{font-size:13px;color:#d0e2f0}
#pf-summary{white-space:pre-wrap;background:#172635;padding:12px;border-radius:5px;line-height:1.7}
.presence-group[hidden]{display:none}.buttons{display:flex;flex-wrap:wrap;gap:7px}
</style></head><body><main><h1>WI-0155 · Presence-first, crop-level review</h1>
<p class="note">PRIVATE OFFLINE EXPERIMENT. Confirm a person from one selected crop; you do not need to merge their other groups. A group may contain different people: mark each representative crop separately, and check "Exclude from provisional group" for a wrongly grouped crop. Exclusion is only a private review annotation: it never rewrites clustering, existing group CSVs or production data.</p>
<p class="note">The gallery shows only source-validated <b>representative crops</b>, NOT every detection. Unshown members and unassigned/noise observations may include other people. Leave unnecessary groups untouched; unreviewed evidence remains counted, not dismissed. No person coverage proof or automatic identity assignment. Use P0001-style private aliases from the same people map; an unknown face has a separate "Real face, unknown person" decision. No source video, database or network access.</p>
<div class="toolbar"><button id="pf-save" type="button">Save private presence-review CSV</button><button id="pf-import" type="button">Import saved CSV</button><input id="pf-file" type="file" accept=".csv,text/csv" hidden><button id="pf-reveal" type="button">Reveal skipped groups</button><span id="pf-status" aria-live="polite"></span></div>
'@)
$html.Add(('<div id="pf-root" data-clip="{0}" data-report="{1}" data-unassigned="{2}" data-total-groups="{3}" data-representative-crops="{4}">' -f
    (HtmlEncode $ClipAlias),(HtmlEncode $reportHash),$UnassignedObservations,$Rows.Count,$records.Count))
$html.Add('<pre id="pf-summary" aria-live="polite"></pre><div class="grid">')
foreach($row in $Rows) {
    $group=[string]$row.Group
    $items=@($records | Where-Object Group -CEQ $group)
    $unshown=[int]$row.Observations-$items.Count
    $rankBlock='No group-level catalogue suggestions available.'
    if ($Rankings.ContainsKey($group)) {
        $suggestions=@($Rankings[$group].topCandidates | ForEach-Object {
            '{0}: {1:F4}' -f (HtmlEncode $_.alias),[double]$_.score
        })
        $rankBlock=($suggestions -join ' · ')
    }
    $html.Add(('<article class="presence-group" data-group="{0}" data-observations="{1}"><h2>{0}</h2><p class="note">{2} representative crop(s) of {1} observations; {3} unshown.</p><p class="rank">Group suggestion only (not verified): {4}</p><div class="buttons"><button type="button" class="pf-skip">Hide group without deciding</button></div>' -f
        (HtmlEncode $group),$row.Observations,$items.Count,$unshown,$rankBlock))
    foreach($item in $items) {
        $html.Add(('<div class="presence-crop" data-group="{0}" data-frame="{1}" data-face="{2}"><img loading="lazy" src="{3}" alt="Face candidate crop; not an identity"><small>Representative observation — classification is independent of the group.</small><label>Human decision<select class="pf-decision"><option value="">Not reviewed</option><option>Confirm person</option><option>Real face unknown</option><option>Not a face</option><option>Cannot tell</option></select></label><label>Known person alias<input class="pf-person" maxlength="5" placeholder="P0001"></label><label><input type="checkbox" class="pf-exclude"> Exclude from provisional group (annotation only)</label><label>Optional private note<textarea class="pf-notes" maxlength="1000"></textarea></label></div>' -f
            (HtmlEncode $group),$item.Frame,$item.Face,(HtmlEncode $item.Relative)))
    }
    $html.Add('</article>')
}
$html.Add('</div></div><p class="note">No completed group list or inferred absence is generated. Saved CSV contains all representative observations including unreviewed rows and binds to the private report SHA-256; it is NOT the legacy group-decision CSV. Confirmed aliases are private proposals only. No database writes.</p><script src="presence-first.js"></script></main></body></html>')
# The in-application review loads this explicitly generated, self-contained
# private bundle in browser memory. It contains representative PNG bytes and
# aliases, never source paths, originals, names, IDs, or database credentials.
# This is an additional output; preserve the original HTML and CSV workflow.
$webGroups = @($Rows | ForEach-Object {
    $row = $_
    $group = [string]$row.Group
    $suggestions = @()
    if ($Rankings.ContainsKey($group)) {
        $suggestions = @($Rankings[$group].topCandidates | ForEach-Object {
            [string]::Format([Globalization.CultureInfo]::InvariantCulture,
                '{0}: {1:F4}', [string]$_.alias, [double]$_.score)
        })
    }
    [pscustomobject][ordered]@{
        alias = $group
        observations = [int]$row.Observations
        suggestions = @($suggestions)
    }
})
$webCrops = [Collections.Generic.List[object]]::new()
$encodedSize = 0
$cropIndex = 0
foreach ($record in $records) {
    $sourceCrop = $Crops[$cropIndex]
    $cropIndex++
    if ([string]$sourceCrop.Group -cne [string]$record.Group -or
        [int]$sourceCrop.Box.frameNumber -ne [int]$record.Frame -or
        [int]$sourceCrop.Box.faceNumber -ne [int]$record.Face -or
        [int]$sourceCrop.FrameWidth -lt 1 -or [int]$sourceCrop.FrameWidth -gt 3840 -or
        [int]$sourceCrop.FrameHeight -lt 1 -or [int]$sourceCrop.FrameHeight -gt 3840 -or
        [int]$sourceCrop.FaceWidth -lt 1 -or
        [int]$sourceCrop.FaceWidth -gt [int]$sourceCrop.FrameWidth + 1 -or
        [int]$sourceCrop.FaceHeight -lt 1 -or
        [int]$sourceCrop.FaceHeight -gt [int]$sourceCrop.FrameHeight + 1) {
        throw 'Native extracted-frame/face geometry is inconsistent.'
    }
    $path = Join-Path $ReviewFolder $record.Relative
    $info = Get-Item -LiteralPath $path -Force
    if ($info.PSIsContainer -or
        ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $info.Length -lt 24 -or $info.Length -gt 1048576) {
        throw 'Private web review crop must be a bounded real PNG file.'
    }
    [byte[]]$bytes = [IO.File]::ReadAllBytes($path)
    $header = [byte[]](137,80,78,71,13,10,26,10)
    for ($i = 0; $i -lt $header.Length; $i++) {
        if ($bytes[$i] -ne $header[$i]) {
            throw 'Private web review crop is not a PNG.'
        }
    }
    $base64 = [Convert]::ToBase64String($bytes)
    $encodedSize += $base64.Length
    if ($encodedSize -gt 24MB) {
        throw 'Private web review crop evidence exceeds the in-browser limit.'
    }
    $webCrops.Add([pscustomobject][ordered]@{
        group = [string]$record.Group
        frame = [string]$record.Frame
        face = [string]$record.Face
        frameWidth = [int]$sourceCrop.FrameWidth
        frameHeight = [int]$sourceCrop.FrameHeight
        faceWidthPx = [int]$sourceCrop.FaceWidth
        faceHeightPx = [int]$sourceCrop.FaceHeight
        imageBase64 = $base64
    })
}
$bundle = [pscustomobject][ordered]@{
    schemaVersion = $(if ($PrivatePeopleMapPath) { 3 } else { 2 })
    mode = 'PresenceFirst'
    clipAlias = $ClipAlias
    reportSha256 = $reportHash
    catalogueSnapshot = $snapshot
    cataloguePeopleCount = $peopleCount
    personBindings = @($reviewBindings)
    unassignedGroupObservations = $UnassignedObservations
    groups = @($webGroups)
    crops = @($webCrops.ToArray())
}
$bundlePath = Join-Path $ReviewFolder 'presence-first-web-review.json'
$bundleJson = $bundle | ConvertTo-Json -Depth 8 -Compress
if ([Text.Encoding]::UTF8.GetByteCount($bundleJson) -gt 24MB) {
    throw 'Private web review bundle exceeds the maximum import size.'
}
[IO.File]::WriteAllText($bundlePath,$bundleJson,
    [Text.UTF8Encoding]::new($false))

$page=Join-Path $ReviewFolder 'presence-review.html'
[IO.File]::WriteAllLines($page,$html,[Text.UTF8Encoding]::new($false))
if($OpenGallery) { Invoke-Item -LiteralPath $page }
[pscustomobject][ordered]@{
    SchemaVersion=1;Mode='PresenceFirst';ClipAlias=$ClipAlias
    GalleryPath=$page;WorksheetPath=$worksheet;WebReviewBundlePath=$bundlePath;ReviewFolder=$ReviewFolder
    Groups=$Rows.Count;Crops=$records.Count
    ObservationsUnassigned=$UnassignedObservations
    UnshownGroupObservations=($Rows | Measure-Object -Property Observations -Sum).Sum-$records.Count
    ProductionAssignmentsMade=0
}
 -or
            -not [Guid]::TryParseExact([string]$_.personId,'D',[ref]$id) -or
            $id -eq [Guid]::Empty -or
            -not $aliases.Add($alias) -or -not $ids.Add($id.ToString('D'))) {
            throw 'Invalid or duplicate canonical alias binding.'
        }
        [pscustomobject][ordered]@{ alias = $alias; personId = $id.ToString('D') }
    })
}
$rowsById=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
foreach($row in $Rows) {
    if ([string]$row.Clip -cne $ClipAlias -or
        [string]$row.Group -cnotmatch '^G(?!000)[0-9]{3}$' -or
        -not $rowsById.TryAdd([string]$row.Group,$row) -or
        [int]$row.Observations -lt 1) {
        throw 'Inconsistent validated provisional groups.'
    }
}
$records=[Collections.Generic.List[object]]::new()
$byGroup=@{}
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($crop in $Crops) {
    $group=[string]$crop.Group
    if (-not $rowsById.ContainsKey($group)) { throw 'Crop group not in source.' }
    $box=$crop.Box
    $frame=[int]$box.frameNumber
    $face=[int]$box.faceNumber
    $key=('{0}:{1}' -f $frame,$face)
    $path=[string]$crop.Relative
    if ($frame -lt 1 -or $frame -gt 180 -or
        $face -lt 1 -or $face -gt 32 -or
        -not $seen.Add($key) -or
        $path -cnotmatch ('^crops/'+[regex]::Escape($ClipAlias)+'-'+[regex]::Escape($group)+'-[0-9]{2}\.png$') -or
        -not (Test-Path -LiteralPath (Join-Path $ReviewFolder $path) -PathType Leaf)) {
        throw 'Incomplete, reused or missing validated representative crop.'
    }
    if (-not $byGroup.ContainsKey($group)) { $byGroup[$group]=0 }
    $byGroup[$group]++
    if ($byGroup[$group] -gt [int]$rowsById[$group].Observations) {
        throw 'More crops than source observations.'
    }
    $records.Add([pscustomobject][ordered]@{
        Clip=$ClipAlias; ReportSha256=$reportHash; Group=$group
        Frame=[string]$frame; Face=[string]$face
        Decision=''; PersonAlias=''; ExcludeFromGroup='False'; ReviewerNotes=''
        Relative=$path
    })
}
foreach($row in $Rows) {
    if (-not $byGroup.ContainsKey([string]$row.Group)) {
        throw 'A source group has no review crop.'
    }
}
$folder=Get-Item -LiteralPath $ReviewFolder -Force
if (-not $folder.PSIsContainer -or
    ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Private review destination is not a local directory.'
}
$worksheet=Join-Path $ReviewFolder 'presence-first-worksheet.csv'
$records | Select-Object Clip,ReportSha256,Group,Frame,Face,Decision,
    PersonAlias,ExcludeFromGroup,ReviewerNotes |
    Export-Csv -LiteralPath $worksheet -Encoding utf8
$js=Join-Path $PSScriptRoot 'VideoFacePresenceFirstReview.js'
if (-not (Test-Path -LiteralPath $js -PathType Leaf)) {
    throw 'Presence-first browser script missing.'
}
Copy-Item -LiteralPath $js -Destination (Join-Path $ReviewFolder 'presence-first.js')
$html=[Collections.Generic.List[string]]::new()
$html.Add(@'
<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; script-src 'self'">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>WI-0155 · Presence-first private review</title>
<style>
:root{color-scheme:dark}body{font:15px system-ui,Segoe UI,sans-serif;color:#eaf1f6;background:#111a24;margin:24px}
main{max-width:1260px;margin:auto}.note{line-height:1.55;color:#c1d2df;max-width:1000px}
button,select,input,textarea{font:inherit;background:#223446;color:#edf4fa;border:1px solid #718b9c;border-radius:4px;padding:7px}
button{cursor:pointer;background:#245d86}.toolbar{display:flex;gap:12px;flex-wrap:wrap;align-items:center}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(330px,1fr));gap:15px;margin-top:18px}
.presence-group{background:#1a2c3d;border:1px solid #5e7689;border-radius:7px;padding:16px}
.presence-group h2{margin:0 0 8px;font-size:18px}.presence-crop{border-top:1px solid #465e72;padding:10px 0;display:grid;gap:6px}
.presence-crop img{max-height:190px;max-width:100%;object-fit:contain;background:#080e15}
label{display:grid;gap:5px}textarea{min-height:40px;resize:vertical}.rank{font-size:13px;color:#d0e2f0}
#pf-summary{white-space:pre-wrap;background:#172635;padding:12px;border-radius:5px;line-height:1.7}
.presence-group[hidden]{display:none}.buttons{display:flex;flex-wrap:wrap;gap:7px}
</style></head><body><main><h1>WI-0155 · Presence-first, crop-level review</h1>
<p class="note">PRIVATE OFFLINE EXPERIMENT. Confirm a person from one selected crop; you do not need to merge their other groups. A group may contain different people: mark each representative crop separately, and check "Exclude from provisional group" for a wrongly grouped crop. Exclusion is only a private review annotation: it never rewrites clustering, existing group CSVs or production data.</p>
<p class="note">The gallery shows only source-validated <b>representative crops</b>, NOT every detection. Unshown members and unassigned/noise observations may include other people. Leave unnecessary groups untouched; unreviewed evidence remains counted, not dismissed. No person coverage proof or automatic identity assignment. Use P0001-style private aliases from the same people map; an unknown face has a separate "Real face, unknown person" decision. No source video, database or network access.</p>
<div class="toolbar"><button id="pf-save" type="button">Save private presence-review CSV</button><button id="pf-import" type="button">Import saved CSV</button><input id="pf-file" type="file" accept=".csv,text/csv" hidden><button id="pf-reveal" type="button">Reveal skipped groups</button><span id="pf-status" aria-live="polite"></span></div>
'@)
$html.Add(('<div id="pf-root" data-clip="{0}" data-report="{1}" data-unassigned="{2}" data-total-groups="{3}" data-representative-crops="{4}">' -f
    (HtmlEncode $ClipAlias),(HtmlEncode $reportHash),$UnassignedObservations,$Rows.Count,$records.Count))
$html.Add('<pre id="pf-summary" aria-live="polite"></pre><div class="grid">')
foreach($row in $Rows) {
    $group=[string]$row.Group
    $items=@($records | Where-Object Group -CEQ $group)
    $unshown=[int]$row.Observations-$items.Count
    $rankBlock='No group-level catalogue suggestions available.'
    if ($Rankings.ContainsKey($group)) {
        $suggestions=@($Rankings[$group].topCandidates | ForEach-Object {
            '{0}: {1:F4}' -f (HtmlEncode $_.alias),[double]$_.score
        })
        $rankBlock=($suggestions -join ' · ')
    }
    $html.Add(('<article class="presence-group" data-group="{0}" data-observations="{1}"><h2>{0}</h2><p class="note">{2} representative crop(s) of {1} observations; {3} unshown.</p><p class="rank">Group suggestion only (not verified): {4}</p><div class="buttons"><button type="button" class="pf-skip">Hide group without deciding</button></div>' -f
        (HtmlEncode $group),$row.Observations,$items.Count,$unshown,$rankBlock))
    foreach($item in $items) {
        $html.Add(('<div class="presence-crop" data-group="{0}" data-frame="{1}" data-face="{2}"><img loading="lazy" src="{3}" alt="Face candidate crop; not an identity"><small>Representative observation — classification is independent of the group.</small><label>Human decision<select class="pf-decision"><option value="">Not reviewed</option><option>Confirm person</option><option>Real face unknown</option><option>Not a face</option><option>Cannot tell</option></select></label><label>Known person alias<input class="pf-person" maxlength="5" placeholder="P0001"></label><label><input type="checkbox" class="pf-exclude"> Exclude from provisional group (annotation only)</label><label>Optional private note<textarea class="pf-notes" maxlength="1000"></textarea></label></div>' -f
            (HtmlEncode $group),$item.Frame,$item.Face,(HtmlEncode $item.Relative)))
    }
    $html.Add('</article>')
}
$html.Add('</div></div><p class="note">No completed group list or inferred absence is generated. Saved CSV contains all representative observations including unreviewed rows and binds to the private report SHA-256; it is NOT the legacy group-decision CSV. Confirmed aliases are private proposals only. No database writes.</p><script src="presence-first.js"></script></main></body></html>')
# The in-application review loads this explicitly generated, self-contained
# private bundle in browser memory. It contains representative PNG bytes and
# aliases, never source paths, originals, names, IDs, or database credentials.
# This is an additional output; preserve the original HTML and CSV workflow.
$webGroups = @($Rows | ForEach-Object {
    $row = $_
    $group = [string]$row.Group
    $suggestions = @()
    if ($Rankings.ContainsKey($group)) {
        $suggestions = @($Rankings[$group].topCandidates | ForEach-Object {
            [string]::Format([Globalization.CultureInfo]::InvariantCulture,
                '{0}: {1:F4}', [string]$_.alias, [double]$_.score)
        })
    }
    [pscustomobject][ordered]@{
        alias = $group
        observations = [int]$row.Observations
        suggestions = @($suggestions)
    }
})
$webCrops = [Collections.Generic.List[object]]::new()
$encodedSize = 0
$cropIndex = 0
foreach ($record in $records) {
    $sourceCrop = $Crops[$cropIndex]
    $cropIndex++
    if ([string]$sourceCrop.Group -cne [string]$record.Group -or
        [int]$sourceCrop.Box.frameNumber -ne [int]$record.Frame -or
        [int]$sourceCrop.Box.faceNumber -ne [int]$record.Face -or
        [int]$sourceCrop.FrameWidth -lt 1 -or [int]$sourceCrop.FrameWidth -gt 3840 -or
        [int]$sourceCrop.FrameHeight -lt 1 -or [int]$sourceCrop.FrameHeight -gt 3840 -or
        [int]$sourceCrop.FaceWidth -lt 1 -or
        [int]$sourceCrop.FaceWidth -gt [int]$sourceCrop.FrameWidth + 1 -or
        [int]$sourceCrop.FaceHeight -lt 1 -or
        [int]$sourceCrop.FaceHeight -gt [int]$sourceCrop.FrameHeight + 1) {
        throw 'Native extracted-frame/face geometry is inconsistent.'
    }
    $path = Join-Path $ReviewFolder $record.Relative
    $info = Get-Item -LiteralPath $path -Force
    if ($info.PSIsContainer -or
        ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $info.Length -lt 24 -or $info.Length -gt 1048576) {
        throw 'Private web review crop must be a bounded real PNG file.'
    }
    [byte[]]$bytes = [IO.File]::ReadAllBytes($path)
    $header = [byte[]](137,80,78,71,13,10,26,10)
    for ($i = 0; $i -lt $header.Length; $i++) {
        if ($bytes[$i] -ne $header[$i]) {
            throw 'Private web review crop is not a PNG.'
        }
    }
    $base64 = [Convert]::ToBase64String($bytes)
    $encodedSize += $base64.Length
    if ($encodedSize -gt 24MB) {
        throw 'Private web review crop evidence exceeds the in-browser limit.'
    }
    $webCrops.Add([pscustomobject][ordered]@{
        group = [string]$record.Group
        frame = [string]$record.Frame
        face = [string]$record.Face
        frameWidth = [int]$sourceCrop.FrameWidth
        frameHeight = [int]$sourceCrop.FrameHeight
        faceWidthPx = [int]$sourceCrop.FaceWidth
        faceHeightPx = [int]$sourceCrop.FaceHeight
        imageBase64 = $base64
    })
}
$bundle = [pscustomobject][ordered]@{
    schemaVersion = 2
    mode = 'PresenceFirst'
    clipAlias = $ClipAlias
    reportSha256 = $reportHash
    catalogueSnapshot = $snapshot
    cataloguePeopleCount = $peopleCount
    unassignedGroupObservations = $UnassignedObservations
    groups = @($webGroups)
    crops = @($webCrops.ToArray())
}
$bundlePath = Join-Path $ReviewFolder 'presence-first-web-review.json'
$bundleJson = $bundle | ConvertTo-Json -Depth 8 -Compress
if ([Text.Encoding]::UTF8.GetByteCount($bundleJson) -gt 24MB) {
    throw 'Private web review bundle exceeds the maximum import size.'
}
[IO.File]::WriteAllText($bundlePath,$bundleJson,
    [Text.UTF8Encoding]::new($false))

$page=Join-Path $ReviewFolder 'presence-review.html'
[IO.File]::WriteAllLines($page,$html,[Text.UTF8Encoding]::new($false))
if($OpenGallery) { Invoke-Item -LiteralPath $page }
[pscustomobject][ordered]@{
    SchemaVersion=1;Mode='PresenceFirst';ClipAlias=$ClipAlias
    GalleryPath=$page;WorksheetPath=$worksheet;WebReviewBundlePath=$bundlePath;ReviewFolder=$ReviewFolder
    Groups=$Rows.Count;Crops=$records.Count
    ObservationsUnassigned=$UnassignedObservations
    UnshownGroupObservations=($Rows | Measure-Object -Property Observations -Sum).Sum-$records.Count
    ProductionAssignmentsMade=0
}
