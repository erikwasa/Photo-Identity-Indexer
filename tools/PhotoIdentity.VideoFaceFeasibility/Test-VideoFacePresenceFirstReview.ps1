<#
.SYNOPSIS
Deterministic Windows regression for the private presence-first gallery and
aggregate verifier. Synthetic image frames; no DB, models, network or real media.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function Check([bool]$Ok,[string]$Message) { if(-not $Ok) { throw $Message } }
function Refused([scriptblock]$Action) {
    try { & $Action | Out-Null; return $false } catch { return $true }
}
function WriteJson($Value,[string]$Path) {
    [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 30),
        [Text.UTF8Encoding]::new($false))
}
function Crop([int]$Frame,[int]$Face,[long]$Time) {
    return @{frameNumber=$Frame;faceNumber=$Face;timestampMs=$Time;
        x=.1;y=.15;width=.3;height=.38;detectorConfidence=.8;quality=.5}
}
$root=Join-Path $env:TEMP ('wi0155-presence-synth-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$gallery=$null
try {
    Add-Type -AssemblyName System.Drawing
    $framesDir=Join-Path $root 'frames'
    New-Item -ItemType Directory -Path $framesDir | Out-Null
    $frames=@()
    for($i=1;$i -le 3;$i++) {
        $name=('frame-{0:D2}.png' -f $i)
        $image=[Drawing.Bitmap]::new(48,64)
        try { $image.Save((Join-Path $framesDir $name),[Drawing.Imaging.ImageFormat]::Png) }
        finally { $image.Dispose() }
        $frames+=@{file=$name;timestampMs=($i*1000)}
    }
    $manifest=Join-Path $framesDir 'frames.json'
    WriteJson @{schemaVersion=1;frames=$frames} $manifest
    $case=Join-Path $root 'cases.json'
    WriteJson @{schemaVersion=1;clips=@(@{alias='V04';manifest=$manifest})} $case
    $groups=@(
        @{alias='G001';observations=2;representativeCrops=@((Crop 1 1 1000),(Crop 2 1 2000))},
        @{alias='G002';observations=1;representativeCrops=@((Crop 1 2 1000))},
        @{alias='G003';observations=1;representativeCrops=@((Crop 3 1 3000))}
    )
    $reportPath=Join-Path $root 'report.json'
    $report=@{
        schemaVersion=1;catalogue=@{pgSnapshot='1:1:';selectedPeople=3;eligiblePeople=3}
        clips=@(@{
            alias='V04';review=@{
                aggregate=@{facesDetected=6;framesExamined=3}
                diagnostics=@{
                    provisionalGroups=@{
                        schemaVersion=1;maximumGroups=64;observationsConsidered=6
                        observationsAssigned=4;observationsUnassigned=2;groups=$groups
                    }
                }
            }
        })
    }
    WriteJson $report $reportPath
    $mapPath=Join-Path $root 'map.json'
    WriteJson @{
        schemaVersion=1;pgSnapshot='1:1:'
        people=@(
            @{alias='P0001';personId='00000000-0000-0000-0000-000000000001';displayName='DO-NOT-LEAK-X'},
            @{alias='P0002';personId='00000000-0000-0000-0000-000000000002';displayName='DO-NOT-LEAK-Y'},
            @{alias='P0003';personId='00000000-0000-0000-0000-000000000003';displayName='DO-NOT-LEAK-Z'}
        )
    } $mapPath
    $inputs=@($reportPath,$mapPath,$case,$manifest)
    $before=@($inputs | ForEach-Object {(Get-FileHash -LiteralPath $_).Hash})
    $script=Join-Path $PSScriptRoot 'New-VideoFaceGroupReview.ps1'
    $gallery=& $script -PrivateReportPath $reportPath -PrivateCasesPath $case -ClipAlias V04 -GroupSource Provisional -PresenceFirst
    Check ($gallery.Mode -ceq 'PresenceFirst' -and $gallery.Groups -eq 3 -and
        $gallery.Crops -eq 4 -and $gallery.ObservationsUnassigned -eq 2 -and
        $gallery.UnshownGroupObservations -eq 0 -and
        $gallery.ProductionAssignmentsMade -eq 0) 'Gallery accounting failed.'
    $html=Get-Content -LiteralPath $gallery.GalleryPath -Raw
    Check ($html.Contains('data-group="G001" data-frame="1" data-face="1"') -and
        $html.Contains('data-group="G001" data-frame="2" data-face="1"') -and
        $html.Contains('data-group="G002" data-frame="1" data-face="2"') -and
        $html.Contains('presence-first.js') -and
        -not $html.Contains('DO-NOT-LEAK') -and
        -not $html.Contains($framesDir)) 'Gallery source binding or privacy failed.'
    Check (@(Get-ChildItem -LiteralPath (Join-Path $gallery.ReviewFolder 'crops') -File).Count -eq 4) 'Crop reconstruction mismatch.'
    Check ((Test-Path -LiteralPath $gallery.WebReviewBundlePath -PathType Leaf) -and
        ((Get-Item -LiteralPath $gallery.WebReviewBundlePath).Length -lt 24MB)) 'Private in-app bundle missing or oversized.'
    $webJson = Get-Content -LiteralPath $gallery.WebReviewBundlePath -Raw
    $web = $webJson | ConvertFrom-Json -Depth 12
    Check ($web.schemaVersion -eq 1 -and $web.mode -ceq 'PresenceFirst' -and
        $web.clipAlias -ceq 'V04' -and
        $web.reportSha256 -ceq (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant() -and
        $web.unassignedGroupObservations -eq 2 -and
        @($web.groups).Count -eq 3 -and
        @($web.crops).Count -eq 4 -and
        $web.crops[0].imageBase64.StartsWith('iVBORw0KGgo') -and
        -not $webJson.Contains('DO-NOT-LEAK') -and
        -not $webJson.Contains($framesDir) -and
        -not $webJson.Contains($mapPath)) 'In-app bundle contains invalid or private source evidence.'
    $records=@(Import-Csv -LiteralPath $gallery.WorksheetPath)
    Check ($records.Count -eq 4 -and
        @($records | Where-Object Decision -CNE '').Count -eq 0) 'Initial blank worksheet invalid.'
    $records[0].Decision='Confirm person';$records[0].PersonAlias='P0001'
    $records[1].Decision='Confirm person';$records[1].PersonAlias='P0002'
    $records[1].ExcludeFromGroup='True'
    $records[2].Decision='Confirm person';$records[2].PersonAlias='P0001'
    # G003 intentionally remains unreviewed, despite other confirmations.
    $reviewed=Join-Path $root 'reviewed.csv'
    $records | Export-Csv -LiteralPath $reviewed -Encoding utf8
    $measure=Join-Path $PSScriptRoot 'Measure-VideoFacePresenceFirstReview.ps1'
    $measurementParams=@{ReviewedCsvPath=$reviewed;PrivateGroupReportPath=$reportPath;
        PrivatePeopleMapPath=$mapPath;ClipAlias='V04'}
    $m=& $measure @measurementParams
    Check ($m.InputEvidenceValidated -and
        $m.KnownPersonPresenceProposals -eq 2 -and
        $m.ConfirmedRepresentativeCrops -eq 3 -and
        $m.DuplicateKnownPersonConfirmations -eq 1 -and
        $m.MixedIdentityGroups -eq 1 -and
        $m.ExcludedCropAnnotations -eq 1 -and
        $m.UnreviewedRepresentativeCrops -eq 1 -and
        $m.GroupsWithoutReviewedCrops -eq 1 -and
        $m.UnassignedGroupObservations -eq 2 -and
        -not $m.PersonCoverageProven -and $m.ProductionAssignmentsMade -eq 0) 'Presence-first aggregate mismatch.'
    $out=$m | ConvertTo-Json -Depth 5
    foreach($secret in @('G001','P0001','DO-NOT-LEAK','reviewed.csv',$framesDir)){
        Check (-not $out.Contains($secret)) 'Private values leaked to aggregate stdout.'
    }
    $records[2].PersonAlias='P9999'
    $records | Export-Csv -LiteralPath $reviewed -Encoding utf8
    Check (Refused { & $measure @measurementParams }) 'Nonexistent private person accepted.'
    $records[2].PersonAlias='P0001'
    $records[3].Group='G999'
    $records | Export-Csv -LiteralPath $reviewed -Encoding utf8
    Check (Refused { & $measure @measurementParams }) 'Forged group accepted.'
    $records[3].Group='G003'
    $records[3].ExcludeFromGroup='True'
    $records | Export-Csv -LiteralPath $reviewed -Encoding utf8
    Check (Refused { & $measure @measurementParams }) 'Silent crop exclusion accepted.'
    $records[3].ExcludeFromGroup='False'
    $records[3].Decision='Not a face'
    $records | Export-Csv -LiteralPath $reviewed -Encoding utf8
    $m2=& $measure @measurementParams
    Check ($m2.KnownPersonPresenceProposals -eq 2 -and
        $m2.BackgroundRepresentativeCrops -eq 1 -and
        $m2.GroupsWithoutReviewedCrops -eq 0) 'Reviewed status lost.'
    $after=@($inputs | ForEach-Object {(Get-FileHash -LiteralPath $_).Hash})
    Check (($before -join ':') -ceq ($after -join ':')) 'Source inputs changed.'
    Check (Refused { & $script -PrivateReportPath $reportPath -PrivateCasesPath $case -ClipAlias V04 -GroupSource CenterFaceShadow -PresenceFirst }) 'Corroboration-only mode accepted.'
    Write-Host 'WI-0155 private presence-first gallery and aggregate synthetic tests: PASS'
} finally {
    # Only generated synthetic output is removed, never a real gallery.
    $ownedRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'PhotoIdentity\WI0155\group-reviews'))
    if ($null -ne $gallery -and $gallery.ReviewFolder -and
        (Test-Path -LiteralPath $gallery.ReviewFolder) -and
        [IO.Path]::GetFullPath($gallery.ReviewFolder).StartsWith(
            ($ownedRoot.TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $gallery.ReviewFolder -Recurse -Force
    }
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
