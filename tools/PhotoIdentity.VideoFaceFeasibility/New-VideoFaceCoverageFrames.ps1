<#
.SYNOPSIS
Create a bounded, private set of timeline-spread JPEG stills from ONE explicitly
approved, already-local video for experimental WI-0155 coverage.
No OneDrive hydration, DB writes, original modification or background jobs.
.EXAMPLE
./tools/PhotoIdentity.VideoFaceFeasibility/New-VideoFaceCoverageFrames.ps1 -VideoPath 'D:\Private\approved-clip.mp4' -FfmpegPath 'C:\Tools\ApprovedFFmpeg\ffmpeg.exe' -ApprovePrivateLocalVideo
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $VideoPath,

    [Parameter(Mandatory = $true)]
    [string] $FfmpegPath,

    [Parameter(Mandatory = $true)]
    [switch] $ApprovePrivateLocalVideo,

    [ValidateSet(12, 24, 36)]
    [int] $FrameCount = 36,

    [ValidateRange(0, 86400)]
    [double] $WindowStartSeconds = 0,

    [ValidateRange(0, 600)]
    [double] $WindowLengthSeconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $ApprovePrivateLocalVideo) {
    throw 'Use -ApprovePrivateLocalVideo only for an expressly approved, fully local video.'
}

$video = [IO.Path]::GetFullPath($VideoPath)
$source = Get-Item -LiteralPath $video -ErrorAction Stop
if ($source.PSIsContainer) { throw 'The approved video must be a regular file.' }
# Windows FILE_ATTRIBUTE_RECALL_ON_OPEN, RECALL_ON_DATA_ACCESS, OFFLINE
# and REPARSE_POINT: reject any source that could require hydration.
$disallowedAttributes = [long]0x00040000 -bor [long]0x00400000 -bor
    [long][IO.FileAttributes]::Offline -bor [long][IO.FileAttributes]::ReparsePoint
if (([long]$source.Attributes -band $disallowedAttributes) -ne 0) {
    throw 'The selected video is linked, offline or may require on-demand hydration.'
}

$ffmpeg = [IO.Path]::GetFullPath($FfmpegPath)
$ffprobe = Join-Path (Split-Path -Parent $ffmpeg) 'ffprobe.exe'
foreach ($exe in @($ffmpeg, $ffprobe)) {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw 'Complete, expressly selected FFmpeg/FFprobe installation required.'
    }
}

# Probe metadata only. This tool never searches directories for videos.
$probeJson = & $ffprobe -v error -show_entries 'format=duration' -of json -i $video
if ($LASTEXITCODE -ne 0) { throw 'FFprobe could not read the selected local video.' }
try {
    $metadata = ($probeJson -join [Environment]::NewLine) | ConvertFrom-Json
    $duration = [double]$metadata.format.duration
} catch {
    throw 'The video does not expose an unambiguous finite duration.'
}
if (-not [double]::IsFinite($duration) -or $duration -le 0) {
    throw 'The video has no finite positive duration.'
}

if ($WindowStartSeconds -ge $duration) {
    throw 'The selected window starts beyond the end of the video.'
}
$remaining = $duration - $WindowStartSeconds
if ($WindowLengthSeconds -eq 0) {
    if ($remaining -gt 600) {
        throw 'The clip exceeds ten minutes. Explicitly choose a window of at most 600 seconds.'
    }
    $window = $remaining
} else {
    $window = $WindowLengthSeconds
}
if ($window -gt 600 -or $window -gt $remaining) {
    throw 'Selected window must fit inside the video and be at most ten minutes.'
}
if ($window -lt ($FrameCount * 0.03)) {
    throw 'Selected window is too short for distinct timestamps at this sampling density.'
}

$privateRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Temp\wi0155\coverage'
New-Item -ItemType Directory -Path $privateRoot -Force | Out-Null
$privateRootInfo = Get-Item -LiteralPath $privateRoot
if (($privateRootInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Private output root must not be a reparse point.'
}

$folderName = 'coverage-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$output = Join-Path $privateRoot $folderName
New-Item -ItemType Directory -Path $output -ErrorAction Stop | Out-Null

$frames = [System.Collections.Generic.List[object]]::new()
$previousTimestamp = -1L
for ($index = 0; $index -lt $FrameCount; $index++) {
    # Stratified midpoint samples span the entire selected window, not just
    # the first two seconds. Short appearances may still be missed.
    $relativeSeconds = (($index + 0.5) * $window / $FrameCount)
    $absoluteSeconds = $WindowStartSeconds + $relativeSeconds
    $timestampMs = [long][math]::Round($relativeSeconds * 1000)
    if ($timestampMs -le $previousTimestamp -or $timestampMs -gt 600000) {
        throw 'Coverage timestamps must increase strictly and not exceed 600000 ms.'
    }
    $previousTimestamp = $timestampMs

    $file = 'frame-{0:D2}.jpg' -f ($index + 1)
    $destination = Join-Path $output $file
    $seek = $absoluteSeconds.ToString('F3', [Globalization.CultureInfo]::InvariantCulture)
    & $ffmpeg -hide_banner -nostdin -loglevel error -ss $seek -i $video -map '0:v:0' -an -sn -dn -frames:v 1 -threads 1 -vf 'scale=1600:1600:force_original_aspect_ratio=decrease:force_divisible_by=2' -q:v 3 -n $destination
    if ($LASTEXITCODE -ne 0) {
        throw 'FFmpeg frame extraction failed. Private partial output remains for local cleanup.'
    }

    $result = Get-Item -LiteralPath $destination -ErrorAction Stop
    if ($result.Length -lt 3 -or $result.Length -gt (8 * 1024 * 1024)) {
        throw 'Extracted sample violates the 8 MiB still-image limit.'
    }
    $frames.Add([ordered]@{ timestampMs = $timestampMs; file = $file })
}

@{
    schemaVersion = 1
    frames = @($frames)
} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $output 'frames.json') -Encoding utf8

Write-Host 'Private WI-0155 coverage samples created.'
Write-Host ('Frame count: {0}' -f $frames.Count)
Write-Host ('Window length (seconds): {0:F2}' -f $window)
Write-Host ('Local output: {0}' -f $output)
Write-Host 'No person identities have been inferred; short appearances may still be missed.'
[pscustomobject]@{
    ManifestPath = Join-Path $output 'frames.json'
    FramesDirectory = $output
    FrameCount = $frames.Count
    WindowStartSeconds = $WindowStartSeconds
    WindowLengthSeconds = $window
}
