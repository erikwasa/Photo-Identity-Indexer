#requires -Version 7.0
<#
.SYNOPSIS
  Private, read-only video codec inventory for WI-0150.
.DESCRIPTION
  Never probes OneDrive files marked offline or recall-on-access.
  Uses a seeded reservoir sample per extension and writes only aggregate, path-free fields.
  The raw source is never copied, moved, changed, or uploaded.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArchiveRoot,

    [Parameter(Mandatory = $true)]
    [string]$FfprobePath,

    [ValidateRange(1, 200)]
    [int]$SamplesPerExtension = 16,

    [ValidateRange(1, 120)]
    [int]$ProbeTimeoutSeconds = 15,

    [string]$OutputPath = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PhotoIdentity\diagnostics\video-codec-survey.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ArchiveRoot -PathType Container)) {
    throw 'ArchiveRoot must be an existing directory.'
}
$probeCommand = Get-Command -Name $FfprobePath -CommandType Application -ErrorAction Stop
$probeExecutable = $probeCommand.Source
$extensions = @('.mov', '.mp4')
$stats = [ordered]@{}
$samples = @{}
foreach ($extension in $extensions) {
    $stats[$extension] = [ordered]@{
        extension = $extension
        enumerated = 0
        locally_available = 0
        skipped_online_or_uncertain = 0
        probed_ok = 0
        probe_failed = 0
        timed_out = 0
    }
    $samples[$extension] = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
}
$random = [System.Random]::new(150)

# OneDrive placeholder bits used by the existing source adapter.
$recallOnOpen = 0x00040000
$recallOnDataAccess = 0x00400000
$offline = [int][System.IO.FileAttributes]::Offline

foreach ($file in (Get-ChildItem -LiteralPath $ArchiveRoot -File -Recurse -ErrorAction Stop)) {
    $extension = $file.Extension.ToLowerInvariant()
    if (-not $stats.Contains($extension)) {
        continue
    }

    $record = $stats[$extension]
    $record['enumerated'] = [int]$record['enumerated'] + 1
    $flags = [int]$file.Attributes
    if ((($flags -band $recallOnOpen) -ne 0) -or
        (($flags -band $recallOnDataAccess) -ne 0) -or
        (($flags -band $offline) -ne 0)) {
        $record['skipped_online_or_uncertain'] = [int]$record['skipped_online_or_uncertain'] + 1
        continue
    }

    # Do not trust generic ReparsePoint entries that lack an explicit locally-pinned flag.
    $isReparsePoint = ($flags -band [int][System.IO.FileAttributes]::ReparsePoint) -ne 0
    $isPinned = ($flags -band 0x00080000) -ne 0
    if ($isReparsePoint -and -not $isPinned) {
        $record['skipped_online_or_uncertain'] = [int]$record['skipped_online_or_uncertain'] + 1
        continue
    }

    $record['locally_available'] = [int]$record['locally_available'] + 1
    $reservoir = $samples[$extension]
    if ($reservoir.Count -lt $SamplesPerExtension) {
        $reservoir.Add($file)
    }
    else {
        $replacement = $random.Next([int]$record['locally_available'])
        if ($replacement -lt $SamplesPerExtension) {
            $reservoir[$replacement] = $file
        }
    }
}

function Get-VideoProbe {
    param(
        [string]$Executable,
        [string]$SourcePath,
        [int]$TimeoutSeconds
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $arguments = @(
        '-v', 'error',
        '-protocol_whitelist', 'file',
        '-probesize', '1048576',
        '-analyzeduration', '1500000',
        '-show_format', '-show_streams',
        '-show_entries',
        'format=format_name,duration:format_tags=creation_time,com.apple.quicktime.creationdate,location,com.apple.quicktime.location.ISO6709:stream=codec_type,codec_name,profile,width,height,pix_fmt,avg_frame_rate,color_primaries,color_transfer:stream_tags=rotate,creation_time:stream_side_data=rotation',
        '-of', 'json', '-i', $SourcePath
    )
    foreach ($argument in $arguments) {
        [void]$start.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            [void]$process.WaitForExit()
            return [pscustomobject]@{ outcome = 'timeout'; data = $null }
        }
        # Drain both pipes; never put raw diagnostic text (which can include paths) into the report.
        $json = $stdout.GetAwaiter().GetResult()
        [void]$stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            return [pscustomobject]@{ outcome = 'probe_failed'; data = $null }
        }
        return [pscustomobject]@{ outcome = 'ok'; data = ($json | ConvertFrom-Json -Depth 15) }
    }
    catch {
        return [pscustomobject]@{ outcome = 'probe_failed'; data = $null }
    }
    finally {
        $process.Dispose()
    }
}

function Get-OptionalValue {
    param($Object, [string]$Name)

    if ($null -eq $Object) {
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($extension in $extensions) {
    foreach ($file in $samples[$extension]) {
        $probeResult = Get-VideoProbe -Executable $probeExecutable -SourcePath $file.FullName -TimeoutSeconds $ProbeTimeoutSeconds
        $record = $stats[$extension]
        if ($probeResult.outcome -ne 'ok') {
            $record[$probeResult.outcome] = [int]$record[$probeResult.outcome] + 1
            continue
        }
        $record['probed_ok'] = [int]$record['probed_ok'] + 1
        $probe = $probeResult.data
        $streams = @(Get-OptionalValue -Object $probe -Name 'streams')
        $video = @($streams | Where-Object { $null -ne $_ -and $_.codec_type -eq 'video' } | Select-Object -First 1)
        $audio = @($streams | Where-Object { $null -ne $_ -and $_.codec_type -eq 'audio' } | Select-Object -First 1)
        $format = Get-OptionalValue -Object $probe -Name 'format'

        $videoStream = if ($video.Count -gt 0) { $video[0] } else { $null }
        $audioStream = if ($audio.Count -gt 0) { $audio[0] } else { $null }
        $videoTags = Get-OptionalValue -Object $videoStream -Name 'tags'
        $rotation = Get-OptionalValue -Object $videoTags -Name 'rotate'
        $sideList = Get-OptionalValue -Object $videoStream -Name 'side_data_list'
        foreach ($side in @($sideList)) {
            $sideRotation = Get-OptionalValue -Object $side -Name 'rotation'
            if ($null -ne $sideRotation) {
                $rotation = $sideRotation
                break
            }
        }

        $seconds = 0.0
        $hasDuration = [double]::TryParse(
            [string](Get-OptionalValue -Object $format -Name 'duration'),
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$seconds)
        $durationBucket = 'unknown'
        if ($hasDuration) {
            if ($seconds -lt 15) { $durationBucket = 'under_15_seconds' }
            elseif ($seconds -lt 120) { $durationBucket = '15_seconds_to_2_minutes' }
            elseif ($seconds -lt 600) { $durationBucket = '2_to_10_minutes' }
            else { $durationBucket = 'over_10_minutes' }
        }

        $formatTags = Get-OptionalValue -Object $format -Name 'tags'
        $tagNames = if ($null -ne $formatTags) { @($formatTags.PSObject.Properties.Name) } else { @() }
        $results.Add([ordered]@{
            extension = $extension
            container = [string](Get-OptionalValue -Object $format -Name 'format_name')
            video_codec = [string](Get-OptionalValue -Object $videoStream -Name 'codec_name')
            video_profile = [string](Get-OptionalValue -Object $videoStream -Name 'profile')
            pixel_format = [string](Get-OptionalValue -Object $videoStream -Name 'pix_fmt')
            color_transfer = [string](Get-OptionalValue -Object $videoStream -Name 'color_transfer')
            audio_codec = [string](Get-OptionalValue -Object $audioStream -Name 'codec_name')
            display_width = (Get-OptionalValue -Object $videoStream -Name 'width')
            display_height = (Get-OptionalValue -Object $videoStream -Name 'height')
            rotation_degrees = $rotation
            average_frame_rate = [string](Get-OptionalValue -Object $videoStream -Name 'avg_frame_rate')
            duration_bucket = $durationBucket
            creation_time_present = [bool](@($tagNames | Where-Object { $_ -match 'creation' }).Count -gt 0)
            location_tag_present = [bool](@($tagNames | Where-Object { $_ -match '(gps|location)' }).Count -gt 0)
        })
    }
}

$report = [ordered]@{
    schema_version = 1
    generated_at_utc = (Get-Date).ToUniversalTime().ToString('o')
    sampling_policy = 'seeded reservoir per extension; confirmed-local only; no raw tags/paths'
    statistics = @($extensions | ForEach-Object { $stats[$_] })
    sampled_media = @($results)
    limitations = 'Sparse sample; probe timeouts/failures do not establish unsupported codecs. Local cloud placeholder classification is conservative.'
}

$parent = Split-Path -Parent $OutputPath
if ($parent) {
    [void](New-Item -ItemType Directory -Path $parent -Force)
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Private aggregate report saved outside the repository: $OutputPath"
foreach ($row in $report.statistics) {
    Write-Host "$($row.extension): enumerated=$($row.enumerated) local=$($row.locally_available) skipped=$($row.skipped_online_or_uncertain) probed=$($row.probed_ok) failed=$($row.probe_failed) timed-out=$($row.timed_out)"
}
