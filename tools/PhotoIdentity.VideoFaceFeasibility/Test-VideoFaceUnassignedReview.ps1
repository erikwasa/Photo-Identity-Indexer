<#
.SYNOPSIS
Synthetic Windows PowerShell 7.6 tests for the WI-0155 unassigned-audit review.
No real identities, models, videos or database.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This synthetic gallery test requires Windows.' }
Add-Type -AssemblyName System.Drawing
function Check([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}
function MustReject([scriptblock]$Action, [string]$Reason) {
    $rejected=$false
    try { & $Action | Out-Null } catch { $rejected=$true }
    Check $rejected $Reason
}
$root = Join-Path $env:TEMP ('wi0155-unassigned-test-' + [Guid]::NewGuid().ToString('N'))
$private = Join-Path $root 'private'
$images = Join-Path $root 'frames'
New-Item -ItemType Directory -Path $private,$images | Out-Null
$galleryFolder = $null

try {
    $frames = [Collections.Generic.List[object]]::new()
    $samples = [Collections.Generic.List[object]]::new()
    $hashes = @{}
    for ($i=0; $i -lt 12; $i++) {
        $filename = 'frame-{0:D3}.png' -f ($i+1)
        $imagePath = Join-Path $images $filename
        $image = [Drawing.Bitmap]::new(320,240)
        try { $image.Save($imagePath,[Drawing.Imaging.ImageFormat]::Png) }
        finally { $image.Dispose() }
        $hashes[$imagePath]=(Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash
        $frames.Add(@{timestampMs=($i*1000);file=$filename})
        $samples.Add(@{
            frameNumber=$i+1;timestampMs=$i*1000;faceNumber=2
            x=0.1;y=0.2;width=0.35;height=0.40
            detectorConfidence=$(switch ($i) { 1 {.4} 2 {.55} 5 {.75} default {.91} })
            quality=$(switch ($i) { 1 {.2} 2 {.35} 5 {.70} default {.66} })
        })
    }
    $manifestPath = Join-Path $images 'frames.json'
    @{schemaVersion=1;frames=@($frames)} | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8
    $casesPath = Join-Path $private 'cases.json'
    @{schemaVersion=1;clips=@(
        @{alias='V03';manifest=$manifestPath},
        @{alias='V01';manifest=$manifestPath}
    )} | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $casesPath -Encoding utf8

    $groups = @(1..64 | ForEach-Object {
        @{alias=('G{0:D3}' -f $_);observations=1;representativeCrops=@()}
    })
    $audit = @{
        schemaVersion=1;totalUnassignedObservations=120;maximumReviewSamples=96
        temporalStrata=12;strataRepresented=12;sampledCrops=@($samples)
    }
    $provisional = @{
        schemaVersion=1;observationsConsidered=184
        observationsAssigned=64;observationsUnassigned=120
        maximumGroups=64;minimumWithinGroupCosine=.82;groups=$groups
        unassignedAudit=$audit
    }
    $report = @{
        schemaVersion=1;clips=@(
            @{alias='V03';review=@{
                aggregate=@{framesExamined=12;facesDetected=184}
                diagnostics=@{provisionalGroups=$provisional}
            }},
            @{alias='V01';review=@{
                aggregate=@{framesExamined=12;facesDetected=0}
                diagnostics=@{provisionalGroups=$null}
            }}
        )
    }
    $reportPath = Join-Path $private 'audit.json'
    $report | ConvertTo-Json -Depth 18 |
        Set-Content -LiteralPath $reportPath -Encoding utf8

    $generator = Join-Path $PSScriptRoot 'New-VideoFaceUnassignedReview.ps1'
    $measurer = Join-Path $PSScriptRoot 'Measure-VideoFaceUnassignedReview.ps1'
    $gallery = & $generator -PrivateReportPath $reportPath -PrivateCasesPath $casesPath
    $galleryFolder = Split-Path -Parent $gallery.GalleryPath
    Check (Test-Path -LiteralPath $gallery.GalleryPath -PathType Leaf) 'HTML gallery not created.'
    Check (Test-Path -LiteralPath $gallery.WorksheetPath -PathType Leaf) 'CSV worksheet not created.'
    Check ($gallery.SampledCrops -eq 12 -and
        $gallery.TotalUnassignedObservations -eq 120 -and
        $gallery.UnrepresentedObservations -eq 108 -and
        $gallery.TemporalStrataRepresented -eq 12) 'Audit accounting wrong.'

    $html = Get-Content -LiteralPath $gallery.GalleryPath -Raw
    Check ($html.Contains('data-clip="V03"') -and
        $html.Contains('data-report-sha256="') -and
        $html.Contains('crops/S001.png') -and
        $html.Contains('context/S012.jpg') -and
        $html.Contains('Save reviewed CSV') -and
        $html.Contains('Not a face') -and
        $html.Contains('These are NOT all unassigned detections')) 'Offline review controls missing.'
    Check ((@(Get-ChildItem -LiteralPath (Join-Path $galleryFolder 'crops') -File).Count) -eq 12) 'Incorrect crop count.'
    Check ((@(Get-ChildItem -LiteralPath (Join-Path $galleryFolder 'context') -File).Count) -eq 12) 'Missing context images.'
    foreach ($file in $hashes.Keys) {
        Check ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ceq
            $hashes[$file]) 'Gallery modified an approved source image.'
    }

    $worksheet = @(Import-Csv -LiteralPath $gallery.WorksheetPath)
    Check ($worksheet.Count -eq 12 -and $worksheet[0].Sample -ceq 'S001' -and
        $worksheet[0].Frame -eq 1 -and $worksheet[0].Face -eq 2) 'Incorrect worksheet provenance.'
    $initial = & $measurer -ReviewedCsvPath $gallery.WorksheetPath -PrivateGroupReportPath $reportPath -ClipAlias V03
    Check ($initial.UnreviewedSamples -eq 12 -and
        $initial.RealFaceSamples -eq 0 -and $initial.SamplingIsQualityBiased) 'Unreviewed metrics incorrect.'

    $worksheet[0].Label='Real face'; $worksheet[0].PersonAlias='P0001'
    $worksheet[1].Label='Not a face'
    $worksheet[2].Label='Real face'; $worksheet[2].PersonAlias='P0002'
    $worksheet[3].Label='Cannot tell'
    $worksheet[4].Label='Real face'; $worksheet[4].PersonAlias='UNKNOWN'
    $worksheet[5].Label='Not a face'
    $reviewPath = Join-Path $private 'classified.csv'
    $worksheet | Export-Csv -LiteralPath $reviewPath -NoTypeInformation -Encoding utf8
    $stats = & $measurer -ReviewedCsvPath $reviewPath -PrivateGroupReportPath $reportPath -ClipAlias V03
    Check ($stats.SourceEvidenceValidated -and
        $stats.SampledObservations -eq 12 -and
        $stats.TotalUnassignedObservations -eq 120 -and
        $stats.ReviewedSamples -eq 6 -and
        $stats.RealFaceSamples -eq 3 -and
        $stats.BackgroundSamples -eq 2 -and
        $stats.CannotTellSamples -eq 1 -and
        $stats.UnreviewedSamples -eq 6 -and
        $stats.DistinctKnownPeopleInSample -eq 2 -and
        $stats.RealFaceSamplesWithKnownPersonAlias -eq 2 -and
        $stats.RealFaceSamplesWithUnknownPerson -eq 1 -and
        $stats.RealFaceSamplesAtDetectorConfidence070 -eq 2 -and
        $stats.BackgroundSamplesAtDetectorConfidence070 -eq 1 -and
        $stats.RealFaceSamplesAtCropQuality050 -eq 2 -and
        $stats.BackgroundSamplesAtCropQuality050 -eq 1 -and
        -not $stats.SuppressionRulesValidated) 'Labelled audit aggregate measurements incorrect.'
    Check (-not (($stats | Out-String) -match 'P0001|P0002|S00[1-9]|frame-')) 'Private sample details leaked.'

    $worksheet[0].PersonAlias='=HYPERLINK("http://unsafe")'
    $tamperPath = Join-Path $private 'tamper.csv'
    $worksheet | Export-Csv -LiteralPath $tamperPath -NoTypeInformation -Encoding utf8
    MustReject { & $measurer -ReviewedCsvPath $tamperPath -PrivateGroupReportPath $reportPath } 'Formula injection was accepted.'

    $worksheet[0].PersonAlias='P0001'
    $worksheet[1].Frame='999'
    $worksheet | Export-Csv -LiteralPath $tamperPath -NoTypeInformation -Encoding utf8
    MustReject { & $measurer -ReviewedCsvPath $tamperPath -PrivateGroupReportPath $reportPath } 'Tampered frame ordinal was accepted.'

    $worksheet[1].Frame='2'
    $worksheet[1].Label='Not a face'; $worksheet[1].PersonAlias='P0001'
    $worksheet | Export-Csv -LiteralPath $tamperPath -NoTypeInformation -Encoding utf8
    MustReject { & $measurer -ReviewedCsvPath $tamperPath -PrivateGroupReportPath $reportPath } 'Background was assigned a person alias.'

    $worksheet[1].PersonAlias=''
    $worksheet[0].AuditReportSha256 = ('a' * 64)
    $worksheet | Export-Csv -LiteralPath $tamperPath -NoTypeInformation -Encoding utf8
    MustReject { & $measurer -ReviewedCsvPath $tamperPath -PrivateGroupReportPath $reportPath } 'Review from a different report fingerprint was accepted.'

    $audit.sampledCrops[0].x=.95
    $badReport = Join-Path $private 'out-of-bounds.json'
    $report | ConvertTo-Json -Depth 18 |
        Set-Content -LiteralPath $badReport -Encoding utf8
    MustReject { & $generator -PrivateReportPath $badReport -PrivateCasesPath $casesPath } 'Invalid crop geometry accepted.'
    $audit.sampledCrops[0].x=.1

    $audit.strataRepresented=11
    $badStrata=Join-Path $private 'bad-strata.json'
    $report | ConvertTo-Json -Depth 18 |
        Set-Content -LiteralPath $badStrata -Encoding utf8
    MustReject { & $generator -PrivateReportPath $badStrata -PrivateCasesPath $casesPath } 'Wrong temporal strata accepted.'
    $audit.strataRepresented=12

    $audit.totalUnassignedObservations=119
    $badCount=Join-Path $private 'bad-count.json'
    $report | ConvertTo-Json -Depth 18 |
        Set-Content -LiteralPath $badCount -Encoding utf8
    MustReject { & $measurer -ReviewedCsvPath $reviewPath -PrivateGroupReportPath $badCount } 'Changed unassigned total was accepted.'

    Write-Host 'WI-0155 unassigned audit gallery and measurement synthetic tests: PASS'
}
finally {
    if ($galleryFolder -and (Test-Path -LiteralPath $galleryFolder)) {
        Remove-Item -LiteralPath $galleryFolder -Force -Recurse
    }
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Force -Recurse
    }
}
