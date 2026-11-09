<#
.SYNOPSIS
Windows PowerShell 7.6 synthetic WI-0155 visual gallery verification.
No private videos, PostgreSQL or network services are accessed.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'New-VideoFaceReviewGallery.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('wi0155-gallery-test-' + [guid]::NewGuid().ToString('N'))
$oldLocalAppData = $env:LOCALAPPDATA

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function New-SyntheticClip {
    param([string] $Alias, [bool] $Overflow)
    $count = if ($Overflow) { 32 } else { 1 }
    $rawCount = if ($Overflow) { 33 } else { 1 }
    $ranked = @(
        for ($i = 1; $i -le $count; $i++) {
            @{
                frameNumber = 1; timestampMs = 1000; faceNumber = $i
                topReferenceAlias = 'P01'
                topSimilarity = if ($i -eq 1) { 0.85 } else { 0.41 }
            }
        }
        @{
            frameNumber = 2; timestampMs = 2000; faceNumber = 1
            topReferenceAlias = 'P01'; topSimilarity = 0.54
        }
    )
    $findings = @(
        foreach ($candidate in $ranked) {
            @{
                frameNumber = $candidate.frameNumber
                timestampMs = $candidate.timestampMs
                faceNumber = $candidate.faceNumber
                similarity = $candidate.topSimilarity
                reviewStatus = if ($candidate.topSimilarity -ge 0.8) {
                    'review-candidate'
                } else { 'below-exploratory-threshold' }
            }
        }
    )
    return @{
        alias = $Alias
        personRankSummaries = @(
            @{
                alias = 'P01'
                topRankObservations = $ranked.Count
                bestTopRankSimilarity = 0.85
                strongReviewCandidates = 1
            }
        )
        review = @{
            aggregate = @{
                framesExamined = 2
                facesDetected = $ranked.Count
            }
            diagnostics = @{
                frameDetections = @(
                    @{
                        frameNumber = 1; timestampMs = 1000
                        facesDetected = $count
                        rawCandidatesDetected = $rawCount
                        candidatesDiscarded = $rawCount - $count
                    }
                    @{
                        frameNumber = 2; timestampMs = 2000
                        facesDetected = 1
                        rawCandidatesDetected = 1
                        candidatesDiscarded = 0
                    }
                )
                rankedFaces = $ranked
            }
            findings = $findings
        }
    }
}

try {
    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
    $env:LOCALAPPDATA = Join-Path $testRoot 'LocalAppData'
    New-Item -ItemType Directory -Path $env:LOCALAPPDATA -Force | Out-Null
    Add-Type -AssemblyName System.Drawing
    $casesDir = Join-Path $testRoot 'cases'
    New-Item -ItemType Directory -Path $casesDir | Out-Null
    $caseClips = @()
    $originalHashes = @{}

    foreach ($alias in @('V01','V02')) {
        $folder = Join-Path $testRoot $alias
        New-Item -ItemType Directory -Path $folder | Out-Null
        $frames = @()
        for ($i = 1; $i -le 2; $i++) {
            $filename = 'frame-{0:D3}.png' -f $i
            $imagePath = Join-Path $folder $filename
            $bitmap = [Drawing.Bitmap]::new(64,96)
            try {
                $bitmap.Save($imagePath,[Drawing.Imaging.ImageFormat]::Png)
            } finally {
                $bitmap.Dispose()
            }
            $originalHashes[$imagePath] = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash
            $frames += @{ timestampMs = $i * 1000; file = $filename }
        }
        $manifestPath = Join-Path $folder 'frames.json'
        @{ schemaVersion = 1; frames = $frames } |
            ConvertTo-Json -Depth 6 |
            Set-Content -LiteralPath $manifestPath -Encoding utf8
        $caseClips += @{ alias = $alias; manifest = $manifestPath }
    }

    $casesPath = Join-Path $casesDir 'cases.json'
    @{
        schemaVersion = 1
        people = @(
            @{ alias = 'P01'; personId = [guid]::NewGuid() },
            @{ alias = 'P02'; personId = [guid]::NewGuid() }
        )
        clips = $caseClips
    } | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $casesPath -Encoding utf8

    $reportPath = Join-Path $casesDir 'private-report.json'
    $report = @{
        schemaVersion = 1
        clips = @(
            (New-SyntheticClip -Alias 'V01' -Overflow $true),
            (New-SyntheticClip -Alias 'V02' -Overflow $false)
        )
    }
    $report | ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath $reportPath -Encoding utf8

    $output = & $scriptPath -PrivateCasesPath $casesPath -PrivateReportPath $reportPath -FramesPerPerson 1 -UncertainFramesPerClip 1 -OverflowFramesPerClip 1

    Assert-True (Test-Path -LiteralPath $output.GalleryPath -PathType Leaf) 'Gallery HTML missing.'
    Assert-True (Test-Path -LiteralPath $output.WorksheetPath -PathType Leaf) 'Review CSV missing.'
    Assert-True ($output.Previews -ge 2 -and $output.Previews -le 4) 'Unexpected thumbnail count.'

    $html = Get-Content -LiteralPath $output.GalleryPath -Raw
    Assert-True ($html.Contains('Overflow / omissions')) 'Missing overflow section.'
    Assert-True ($html.Contains('P02')) 'Missing person with no ranked observations.'
    Assert-True ($html.Contains('Ranked: P01')) 'Missing tentative ranked shortlist.'
    Assert-True ($html.Contains('thumbnails/')) 'Missing thumbnail references.'

    $worksheet = @(Import-Csv -LiteralPath $output.WorksheetPath)
    Assert-True ($worksheet.Count -ge 3 -and $worksheet.Count -le 8) 'Worksheet is not bounded.'
    Assert-True (@($worksheet | Where-Object Section -eq 'Overflow / omissions').Count -eq 1) 'Missing overflow worksheet entry.'
    foreach ($path in $originalHashes.Keys) {
        Assert-True ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $originalHashes[$path]) 'Source PNG changed.'
    }

    $report.clips[0].review.diagnostics.rankedFaces[0].frameNumber = 99
    $invalidPath = Join-Path $casesDir 'invalid-report.json'
    $report | ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath $invalidPath -Encoding utf8
    $refused = $false
    try {
        & $scriptPath -PrivateCasesPath $casesPath -PrivateReportPath $invalidPath | Out-Null
    } catch {
        $refused = $true
    }
    Assert-True $refused 'Out-of-manifest frame reference was not rejected.'
    Write-Host 'WI-0155 offline gallery synthetic tests: PASS'
} finally {
    $env:LOCALAPPDATA = $oldLocalAppData
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
