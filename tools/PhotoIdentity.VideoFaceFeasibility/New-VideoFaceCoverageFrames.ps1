<#
.SYNOPSIS
Create a bounded, private set of timeline-spread JPEG or lossless PNG stills from ONE explicitly
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

    [ValidateSet(12, 24, 36, 120, 180)]
    [int] $FrameCount = 36,

    [ValidateSet('LegacyJpeg', 'NativeLosslessPng')]
    [string] $FrameEncoding = 'LegacyJpeg',

    [ValidateRange(8, 2048)]
    [int] $MaximumWorkspaceMiB = 1024,

    [ValidateRange(0, 86400)]
    [double] $WindowStartSeconds = 0,

    [ValidateRange(0, 600)]
    [double] $WindowLengthSeconds = 0,

    [switch] $AdaptiveSampling,

    [string] $AdaptiveCuesPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $ApprovePrivateLocalVideo) {
    throw 'Use -ApprovePrivateLocalVideo only for an expressly approved, fully local video.'
}
if ($AdaptiveSampling) {
    if ($PSBoundParameters.ContainsKey('FrameCount') -or
        $PSBoundParameters.ContainsKey('WindowLengthSeconds')) {
        throw 'Adaptive mode selects its own frame count and full next window.'
    }
} elseif (-not [string]::IsNullOrWhiteSpace($AdaptiveCuesPath)) {
    throw '-AdaptiveCuesPath requires -AdaptiveSampling.'
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
$probeJson = & $ffprobe -v error -select_streams 'v:0' -show_entries 'format=duration:stream=width,height,codec_name,pix_fmt:stream_side_data=rotation' -of json -i $video
if ($LASTEXITCODE -ne 0) { throw 'FFprobe could not read the selected local video.' }
try {
    $metadata = ($probeJson -join [Environment]::NewLine) | ConvertFrom-Json
    $duration = [double]$metadata.format.duration
    if (@($metadata.streams).Count -ne 1) { throw 'Missing selected video stream.' }
    $sourceWidth = [int]$metadata.streams[0].width
    $sourceHeight = [int]$metadata.streams[0].height
    if ($sourceWidth -lt 1 -or $sourceHeight -lt 1 -or
        $sourceWidth -gt 3840 -or $sourceHeight -gt 3840) {
        throw 'Native-resolution experiment supports sources up to 3840 pixels per axis.'
    }
    $rotation = 0
    $sideDataProperty = $metadata.streams[0].PSObject.Properties['side_data_list']
    if ($null -ne $sideDataProperty) {
        foreach ($sideData in @($sideDataProperty.Value)) {
            if ($null -ne $sideData -and
                $null -ne $sideData.PSObject.Properties['rotation']) {
                $rotation = [int]$sideData.rotation
            }
        }
    }
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
$plannedRelativeTimestampsMs = @()
$coarseFrameCount = $null
$hasMoreWindows = $false
$refinementBudgetReached = $false
$clipDurationMs = $null
$windowEndMs = $null
if ($AdaptiveSampling) {
    # The C# planner is source-free. It returns strict JSON captured locally,
    # while FFprobe and all original-file safety checks remain in this script.
    # The planner's one-hour cap and ten-minute window size are deliberate.
    if ($duration -gt 3600 -or -not [double]::IsFinite($duration)) {
        throw 'Adaptive sampling supports approved clips of at most one hour.'
    }
    $clipDurationMs = [long][math]::Floor($duration * 1000)
    $startMs = [long][math]::Round($WindowStartSeconds * 1000)
    if ([math]::Abs(($WindowStartSeconds * 1000) - $startMs) -gt 0.0001) {
        throw 'Adaptive window starts must be specified to millisecond precision.'
    }
    if ($clipDurationMs -lt 200 -or $startMs -ge $clipDurationMs) {
        throw 'Adaptive clip duration or window start is out of bounds.'
    }
    if (-not [string]::IsNullOrWhiteSpace($AdaptiveCuesPath)) {
        if (-not [IO.Path]::IsPathFullyQualified($AdaptiveCuesPath)) {
            throw 'Adaptive cue input must be an absolute, private local path.'
        }
        $cueFull = [IO.Path]::GetFullPath($AdaptiveCuesPath)
        $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        $relative = [IO.Path]::GetRelativePath($repoRoot, $cueFull)
        if ($relative -eq '.' -or
            (-not [IO.Path]::IsPathRooted($relative) -and
                $relative -ne '..' -and
                -not $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::Ordinal) -and
                -not $relative.StartsWith('..' + [IO.Path]::AltDirectorySeparatorChar,
                    [StringComparison]::Ordinal))) {
            throw 'Private adaptive cue evidence must be stored outside the repository.'
        }
    }

    $plannerArguments = @(
        'run', '--project',
        (Join-Path $PSScriptRoot 'PhotoIdentity.VideoFaceFeasibility.csproj'),
        '-c', 'Release', '--', '--adaptive-sampling-plan',
        '--duration-ms', $clipDurationMs.ToString([Globalization.CultureInfo]::InvariantCulture),
        '--window-start-ms', $startMs.ToString([Globalization.CultureInfo]::InvariantCulture)
    )
    if (-not [string]::IsNullOrWhiteSpace($AdaptiveCuesPath)) {
        $plannerArguments += @('--private-cues', $cueFull)
    }
    $planJson = & dotnet @plannerArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'The bounded adaptive planner rejected the private request.'
    }
    try {
        $plan = ($planJson -join [Environment]::NewLine) |
            ConvertFrom-Json -ErrorAction Stop
        $windowEndMs = [long]$plan.windowEndMs
        $expectedEnd = [math]::Min($clipDurationMs, ($startMs + 600000L))
        $hasMoreWindows = [bool]$plan.hasMoreWindows
        $refinementBudgetReached = [bool]$plan.refinementBudgetReached
        $coarseFrameCount = [int]$plan.coarseFrameCount
        if ($plan.schemaVersion -ne 1 -or
            [long]$plan.clipDurationMs -ne $clipDurationMs -or
            [long]$plan.windowStartMs -ne $startMs -or
            $windowEndMs -ne $expectedEnd -or
            $hasMoreWindows -ne ($windowEndMs -lt $clipDurationMs)) {
            throw 'Inconsistent adaptive plan provenance.'
        }

        $absoluteTimestamps = @($plan.frameTimestampsMs)
        if ($absoluteTimestamps.Count -lt 1 -or
            $absoluteTimestamps.Count -gt 180 -or
            $coarseFrameCount -lt 1 -or
            $coarseFrameCount -gt $absoluteTimestamps.Count) {
            throw 'Adaptive plan exceeds the frame or coarse-coverage budget.'
        }
        $last = -1L
        $plannedRelativeTimestampsMs = @(
            foreach ($value in $absoluteTimestamps) {
                $time = [long]$value
                if ($time -le $last -or $time -lt $startMs -or
                    $time -ge $windowEndMs) {
                    throw 'Adaptive timestamps are not strictly increasing and in-window.'
                }
                $last = $time
                $time - $startMs
            }
        )
    } catch {
        throw 'Adaptive plan is inconsistent; no private frame workspace was created.'
    }

    $frameTotal = $plannedRelativeTimestampsMs.Count
    $window = ($windowEndMs - $startMs) / 1000.0
} else {
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
    $frameTotal = $FrameCount
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
# Ownership marker is created before the first frame so interrupted runs can be
# inspected and deliberately cleaned. Never adopt pre-existing unmarked folders.
$runId = [Guid]::NewGuid().ToString('D')
$markerPath = Join-Path $output 'wi0155-run.json'
@{
    schemaVersion = 1
    kind = 'PhotoIdentity.WI0155.CoverageWorkspace'
    runId = $runId
    state = 'extracting'
    createdUtc = [DateTime]::UtcNow.ToString('O')
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $markerPath -Encoding utf8

$frames = [System.Collections.Generic.List[object]]::new()
$workspaceBytes = 0L
$workspaceLimitBytes = [long]$MaximumWorkspaceMiB * 1MB
$lossless = $FrameEncoding -eq 'NativeLosslessPng'
$previousTimestamp = -1L
for ($index = 0; $index -lt $frameTotal; $index++) {
    if ($AdaptiveSampling) {
        # Planner timestamps are absolute; frame manifests remain relative to
        # this window so the existing <=600000ms loader stays compatible.
        $timestampMs = [long]$plannedRelativeTimestampsMs[$index]
        $relativeSeconds = $timestampMs / 1000.0
    } else {
        # Preserve the historical fixed, stratified midpoint behavior.
        $relativeSeconds = (($index + 0.5) * $window / $FrameCount)
        $timestampMs = [long][math]::Round($relativeSeconds * 1000)
    }
    $absoluteSeconds = $WindowStartSeconds + $relativeSeconds
    if ($timestampMs -le $previousTimestamp -or $timestampMs -gt 600000) {
        throw 'Coverage timestamps must increase strictly and not exceed 600000 ms.'
    }
    $previousTimestamp = $timestampMs

    $extension = if ($lossless) { 'png' } else { 'jpg' }
    $file = 'frame-{0:D3}.{1}' -f ($index + 1), $extension
    $destination = Join-Path $output $file
    $seek = $absoluteSeconds.ToString('F3', [Globalization.CultureInfo]::InvariantCulture)
    if ($lossless) {
        # FFmpeg autorotation is enabled by default. No scaling or JPEG encoding.
        # PNG rgb24 is lossless *after* the necessary 10-bit YUV to 8-bit RGB conversion.
        & $ffmpeg -hide_banner -nostdin -loglevel error -ss $seek -i $video -map '0:v:0' -an -sn -dn -frames:v 1 -threads 1 -pix_fmt rgb24 -c:v png -n $destination
    } else {
        & $ffmpeg -hide_banner -nostdin -loglevel error -ss $seek -i $video -map '0:v:0' -an -sn -dn -frames:v 1 -threads 1 -vf 'scale=3840:3840:force_original_aspect_ratio=decrease:force_divisible_by=2' -q:v 3 -n $destination
    }
    if ($LASTEXITCODE -ne 0) {
        throw 'FFmpeg frame extraction failed. Private partial output remains for local cleanup.'
    }

    $result = Get-Item -LiteralPath $destination -ErrorAction Stop
    if ($result.Length -lt 3 -or $result.Length -gt (8 * 1024 * 1024)) {
        throw 'Extracted sample violates the 8 MiB still-image limit.'
    }
    $workspaceBytes += $result.Length
    if ($workspaceBytes -gt $workspaceLimitBytes) {
        throw 'Private frame workspace exceeded its configured byte budget. Partial output remains for operator-controlled cleanup.'
    }
    $frames.Add([ordered]@{ timestampMs = $timestampMs; file = $file })
}

@{
    schemaVersion = 1
    frames = @($frames)
} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $output 'frames.json') -Encoding utf8

@{
    schemaVersion = 1
    frameEncoding = $FrameEncoding
    sourceCodec = $metadata.streams[0].codec_name
    sourcePixelFormat = $metadata.streams[0].pix_fmt
    codedWidth = $sourceWidth
    codedHeight = $sourceHeight
    rotationDegrees = $rotation
    expectedDisplayWidth = if ([math]::Abs($rotation) % 180 -eq 90) { $sourceHeight } else { $sourceWidth }
    expectedDisplayHeight = if ([math]::Abs($rotation) % 180 -eq 90) { $sourceWidth } else { $sourceHeight }
    extractedFrameBytes = $workspaceBytes
    maximumWorkspaceMiB = $MaximumWorkspaceMiB
    samplingMode = if ($AdaptiveSampling) { 'Adaptive' } else { 'Fixed' }
    samplingCoarseFrameCount = $coarseFrameCount
    samplingRefinementBudgetReached = $refinementBudgetReached
    samplingHasMoreWindows = $hasMoreWindows
    samplingClipDurationMs = $clipDurationMs
    samplingWindowEndMs = $windowEndMs
    note = 'NativeLosslessPng uses lossless PNG after decoder RGB24 conversion; original 10-bit precision is not preserved.'
} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $output 'extraction-metadata.json') -Encoding utf8

@{
    schemaVersion = 1
    kind = 'PhotoIdentity.WI0155.CoverageWorkspace'
    runId = $runId
    state = 'extracted'
    createdUtc = (Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json).createdUtc
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $markerPath -Encoding utf8

Write-Host 'Private WI-0155 coverage samples created.'
Write-Host ('Frame count: {0}' -f $frames.Count)
Write-Host ('Window length (seconds): {0:F2}' -f $window)
Write-Host ('Local output: {0}' -f $output)
Write-Host 'No person identities have been inferred; short appearances may still be missed.'
[pscustomobject]@{
    RunId = $runId
    ManifestPath = Join-Path $output 'frames.json'
    FramesDirectory = $output
    FrameCount = $frames.Count
    FrameEncoding = $FrameEncoding
    ExtractionMetadataPath = Join-Path $output 'extraction-metadata.json'
    ExtractedFrameBytes = $workspaceBytes
    WindowStartSeconds = $WindowStartSeconds
    WindowLengthSeconds = $window
    SamplingMode = if ($AdaptiveSampling) { 'Adaptive' } else { 'Fixed' }
    CoarseFrameCount = $coarseFrameCount
    RefinementBudgetReached = $refinementBudgetReached
    HasMoreWindows = $hasMoreWindows
    NextWindowStartSeconds = if ($hasMoreWindows) { $windowEndMs / 1000.0 } else { $null }
}
