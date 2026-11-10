<#
.SYNOPSIS
Windows PowerShell 7.6 synthetic tests for WI-0155 group review gallery.
No real identities, models, videos, PostgreSQL, internet or source mutations.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'PowerShell group gallery smoke test is Windows only.' }
Add-Type -AssemblyName System.Drawing
function Check([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw $Message }
}
$root=Join-Path $env:TEMP ('wi0155-group-test-' + [guid]::NewGuid().ToString('N'))
$private=Join-Path $root 'private'
$framesDir=Join-Path $root 'frames'
New-Item -ItemType Directory -Path $private,$framesDir | Out-Null
try {
  $frameRecords=[Collections.Generic.List[object]]::new()
  $hashes=@{}
  for ($i=1; $i -le 5; $i++) {
    $filename=('frame-{0:D2}.png' -f $i)
    $path=Join-Path $framesDir $filename
    $bitmap=[Drawing.Bitmap]::new(160,100)
    try {
      $bitmap.Save($path,[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    $hashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $frameRecords.Add(@{timestampMs=($i*1000);file=$filename})
  }
  $manifest=Join-Path $framesDir 'frames.json'
  @{schemaVersion=1;frames=@($frameRecords)} | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $manifest -Encoding utf8
  $casesPath=Join-Path $private 'cases.json'
  @{schemaVersion=1;people=@(@{alias='P01';personId='00000000-0000-0000-0000-000000000001'});
    clips=@(
      @{alias='V03';manifest=$manifest},
      @{alias='V01';manifest=$manifest}
    )} | ConvertTo-Json -Depth 7 |
    Set-Content -LiteralPath $casesPath -Encoding utf8
  $sample1=@{frameNumber=1;timestampMs=1000;faceNumber=1;
    x=.2;y=.15;width=.3;height=.5;detectorConfidence=.9;quality=.8}
  $sample2=@{frameNumber=2;timestampMs=2000;faceNumber=1;
    x=.25;y=.2;width=.35;height=.4;detectorConfidence=.8;quality=.7}
  $samples=@($sample1,$sample2)
  foreach ($n in 3..5) {
    $samples+=@{frameNumber=$n;timestampMs=($n*1000);faceNumber=1;
      x=.2;y=.15;width=.3;height=.5;detectorConfidence=.9;quality=.8}
  }
  $group=@{alias='G001';observations=5;representativeCrops=$samples}
  $groups=@{schemaVersion=1;observationsConsidered=6;
    observationsAssigned=5;observationsUnassigned=1;
    maximumGroups=64;minimumWithinGroupCosine=.82;groups=@($group)}
  $reportPath=Join-Path $private 'report.json'
  @{schemaVersion=1;clips=@(
      @{alias='V03';review=@{aggregate=@{framesExamined=5;facesDetected=6};
        diagnostics=@{provisionalGroups=$groups}}},
      @{alias='V01';review=@{aggregate=@{framesExamined=5;facesDetected=0};
        diagnostics=@{provisionalGroups=$null}}}
    )} | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath $reportPath -Encoding utf8

  $script=Join-Path $PSScriptRoot 'New-VideoFaceGroupReview.ps1'
  $gallery = & $script -PrivateReportPath $reportPath -PrivateCasesPath $casesPath -ClipAlias V03
  Check (Test-Path -LiteralPath $gallery.GalleryPath -PathType Leaf) 'HTML not generated.'
  Check (Test-Path -LiteralPath $gallery.WorksheetPath -PathType Leaf) 'CSV not generated.'
  Check ($gallery.Groups -eq 1 -and $gallery.Crops -eq 5 -and
    $gallery.UnassignedObservations -eq 1) 'Group/crop/unassigned totals differ.'
  $html=Get-Content -LiteralPath $gallery.GalleryPath -Raw
  Check ($html.Contains('G001') -and $html.Contains('crops/V03-G001-05.png') -and
    $html.Contains('Split required') -and $html.Contains('Save group-review CSV') -and
    $html.Contains('hide-dismissed') -and $html.Contains('repeat-only') -and
    $html.Contains('quick-dismiss')) 'Expected five-crop review and triage controls are missing.'
  $rows=@(Import-Csv -LiteralPath $gallery.WorksheetPath)
  Check ($rows.Count -eq 1 -and $rows[0].Group -eq 'G001') 'Incorrect group worksheet.'
  foreach ($path in $hashes.Keys) {
    Check ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq
      $hashes[$path]) 'Source PNG was modified.'
  }
  # Legacy three-crop reports remain valid so previously generated
  # private group evidence does not require another ONNX evaluation.
  $group.representativeCrops=@($samples | Select-Object -First 3)
  $legacyPath=Join-Path $private 'legacy-three-crop.json'
  @{schemaVersion=1;clips=@(
      @{alias='V03';review=@{aggregate=@{framesExamined=5;facesDetected=6};
        diagnostics=@{provisionalGroups=$groups}}},
      @{alias='V01';review=@{aggregate=@{framesExamined=5;facesDetected=0};
        diagnostics=@{provisionalGroups=$null}}}
    )} | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath $legacyPath -Encoding utf8
  $legacy = & $script -PrivateReportPath $legacyPath -PrivateCasesPath $casesPath -ClipAlias V03
  Check ($legacy.Crops -eq 3) 'Legacy three-crop group reports no longer work.'
  $group.representativeCrops=$samples

  $sample2.x=.9
  $badPath=Join-Path $private 'invalid-report.json'
  @{schemaVersion=1;clips=@(
      @{alias='V03';review=@{aggregate=@{framesExamined=5;facesDetected=6};
        diagnostics=@{provisionalGroups=$groups}}},
      @{alias='V01';review=@{aggregate=@{framesExamined=5;facesDetected=0};
        diagnostics=@{provisionalGroups=$null}}}
    )} | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath $badPath -Encoding utf8
  $refused=$false
  try {
    & $script -PrivateReportPath $badPath -PrivateCasesPath $casesPath -ClipAlias V03 | Out-Null
  } catch { $refused=$true }
  Check $refused 'Out-of-bounds crop was accepted.'
  Write-Host 'WI-0155 group review synthetic tests: PASS'
} finally {
  if (Test-Path -LiteralPath $root) {
    Remove-Item -LiteralPath $root -Recurse -Force
  }
}