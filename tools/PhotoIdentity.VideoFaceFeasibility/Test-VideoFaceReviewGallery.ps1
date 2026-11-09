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
                # V02: best-ranked face is itself below the review
                # threshold, triggering the historic cross-section duplicate.
                # V01: a *different face* in the same frame is uncertain
                # and must remain independently reviewable.
                topSimilarity = if ($i -eq 1) {
                    if ($Alias -eq 'V02') { 0.65 } else { 0.85 }
                } elseif ($i -eq 2) { 0.74 } else { 0.41 }
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
                bestTopRankSimilarity = if ($Alias -eq 'V02') { 0.65 } else { 0.85 }
                strongReviewCandidates = if ($Alias -eq 'V02') { 0 } else { 1 }
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
    Assert-True (Test-Path -LiteralPath $output.SelectionPath -PathType Leaf) 'Review frame selection missing.'
    Assert-True (Test-Path -LiteralPath (Join-Path $output.ReviewFolder 'review-ui.js') -PathType Leaf) 'Offline UI script missing.'
    Assert-True ($html.Contains('class="human-decision"')) 'Interactive review dropdown missing.'
    Assert-True ($html.Contains('class="reviewer-notes"')) 'Reviewer notes editor missing.'
    Assert-True ($html.Contains('review-ui.js')) 'Offline UI script not linked.'
    Assert-True ($html.Contains('Known person - no reliable match')) 'Missing known person missed by matching option.'
    Assert-True ($html.Contains('Frame-level diagnostic only')) 'Missing frame-level overflow guidance.'

    $faceRows = @($worksheet | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Face) })
    $seenFaces = @($faceRows |
        Group-Object { '{0}:{1}:{2}' -f $_.Clip,$_.Frame,$_.Face } |
        Where-Object { $_.Count -gt 1 })
    Assert-True ($seenFaces.Count -eq 0) 'The same face appears in two review sections.'
    Assert-True (@($worksheet | Where-Object {
        $_.Clip -eq 'V02' -and $_.Section -eq 'Uncertain suggestions' -and
        $_.Frame -eq '2' -and $_.Face -eq '1'
    }).Count -eq 1) 'After suppressing V02 duplicate, the next uncertain frame was not selected.'
    Assert-True (@($worksheet | Where-Object {
        $_.Clip -eq 'V01' -and $_.Section -eq 'Uncertain suggestions' -and
        $_.Frame -eq '1' -and $_.Face -eq '2'
    }).Count -eq 1) 'A different uncertain face in the same frame was suppressed.'
    Assert-True (@($worksheet | Where-Object Section -eq 'Overflow / omissions').Count -eq 1) 'Missing overflow worksheet entry.'
    foreach ($path in $originalHashes.Keys) {
        Assert-True ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $originalHashes[$path]) 'Source PNG changed.'
    }


    # Simulate detector-only geometry for the *same selected frames*.
    $geometryFrames = @(
        foreach ($chosen in @((Get-Content -LiteralPath $output.SelectionPath -Raw | ConvertFrom-Json).frames)) {
            $faceCount = if ($chosen.clip -eq 'V01' -and $chosen.frameNumber -eq 1) { 32 } else { 1 }
            $boxes = @(
                for ($face = 1; $face -le $faceCount; $face++) {
                    @{ faceNumber = $face; x = 0.1; y = 0.15; width = 0.2; height = 0.3 }
                }
            )
            @{ clip = $chosen.clip; frameNumber = $chosen.frameNumber; boxes = $boxes }
        }
    )
    $geometryPath = Join-Path $casesDir 'synthetic-geometry.json'
    @{ schemaVersion = 1; frames = $geometryFrames } |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $geometryPath -Encoding utf8

    $withGeometry = & $scriptPath -PrivateCasesPath $casesPath -PrivateReportPath $reportPath -FramesPerPerson 1 -UncertainFramesPerClip 1 -OverflowFramesPerClip 1 -GeometryPath $geometryPath
    Assert-True ($withGeometry.Crops -gt 0) 'Expected face-crop images.'
    $htmlBoxes = Get-Content -LiteralPath $withGeometry.GalleryPath -Raw
    Assert-True ($htmlBoxes.Contains('crops/')) 'Face crop links missing.'
    Assert-True ($htmlBoxes.Contains('overlays/')) 'Bounding-box previews missing.'
    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $withGeometry.ReviewFolder 'crops') -Filter '*.png').Count -gt 0) 'Face crops missing.'
    foreach ($path in $originalHashes.Keys) {
        Assert-True ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $originalHashes[$path]) 'Source frame changed during cropping.'
    }

    # Reject out-of-bounds geometry, not just missing face ordinals.
    $geometryFrames[0].boxes[0].x = 1.2
    $invalidGeometryPath = Join-Path $casesDir 'invalid-geometry.json'
    @{ schemaVersion = 1; frames = $geometryFrames } |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $invalidGeometryPath -Encoding utf8
    $invalidGeometryRefused = $false
    try {
        & $scriptPath -PrivateCasesPath $casesPath -PrivateReportPath $reportPath -FramesPerPerson 1 -UncertainFramesPerClip 1 -OverflowFramesPerClip 1 -GeometryPath $invalidGeometryPath | Out-Null
    } catch {
        $invalidGeometryRefused = $true
    }
    Assert-True $invalidGeometryRefused 'Invalid geometry was not rejected.'

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
