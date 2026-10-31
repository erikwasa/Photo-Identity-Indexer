[CmdletBinding()]
param(
    [string]$ConfigurationPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConfigurationPath)) {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is missing; supply -ConfigurationPath explicitly."
    }
    $ConfigurationPath = Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher.json"
}

if (-not (Test-Path -LiteralPath $ConfigurationPath -PathType Leaf)) {
    throw "The Photo Identity launcher configuration does not exist."
}

$config = Get-Content -LiteralPath $ConfigurationPath -Raw | ConvertFrom-Json
if ($null -eq $config.settings) {
    throw "Launcher settings are missing."
}
$settings = $config.settings

function Get-RequiredSetting {
    param([string]$Name)
    $property = $settings.PSObject.Properties[$Name]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Launcher setting is required: $Name"
    }
    return ([string]$property.Value).Trim()
}

function Test-ApprovedExecutable {
    param(
        [string]$ExecutableName,
        [string]$PathSetting,
        [string]$DigestSetting
    )

    $configuredPath = Get-RequiredSetting $PathSetting
    $expectedSha = Get-RequiredSetting $DigestSetting

    if ($expectedSha -cnotmatch '^[a-f0-9]{64}$') {
        throw "Invalid approved SHA-256 format for $ExecutableName."
    }
    if (-not [IO.Path]::IsPathFullyQualified($configuredPath) -or
        $configuredPath -notmatch '^[A-Za-z]:[\\/]' -or
        [IO.Path]::GetFileName($configuredPath) -ine $ExecutableName) {
        throw "$ExecutableName requires a full local Windows executable path."
    }

    $fullPath = [IO.Path]::GetFullPath($configuredPath)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Approved $ExecutableName is missing."
    }

    # The video CLI checks every ancestor for reparse points.
    # WinGet command aliases, symlinks and junctions must not be accepted.
    for ($cursor = $fullPath; $null -ne $cursor; $cursor = [IO.Path]::GetDirectoryName($cursor)) {
        $attributes = [IO.File]::GetAttributes($cursor)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Approved $ExecutableName path contains a symbolic link or junction."
        }
        if ([IO.Path]::GetPathRoot($cursor) -eq $cursor) {
            break
        }
    }

    $actualSha = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha -cne $expectedSha) {
        throw "Approved $ExecutableName SHA-256 mismatch; review the binary and update approval explicitly."
    }

    return [pscustomobject]@{
        Path = $fullPath
        Sha256 = $expectedSha
    }
}

$ffmpeg = Test-ApprovedExecutable "ffmpeg.exe" "PhotoIdentity__VideoProcessing__FfmpegPath" "PhotoIdentity__VideoProcessing__FfmpegSha256"
$ffprobe = Test-ApprovedExecutable "ffprobe.exe" "PhotoIdentity__VideoProcessing__FfprobePath" "PhotoIdentity__VideoProcessing__FfprobeSha256"

# Read-only: no install, network, launcher writes, process execution or media access.
[pscustomobject]@{
    FfmpegPath = $ffmpeg.Path
    FfmpegSha256 = $ffmpeg.Sha256
    FfprobePath = $ffprobe.Path
    FfprobeSha256 = $ffprobe.Sha256
}
