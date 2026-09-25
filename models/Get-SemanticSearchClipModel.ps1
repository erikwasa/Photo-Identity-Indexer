[CmdletBinding()]
param(
    [Parameter()]
    [string] $OutputDirectory = (Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Models\WI-0126\clip-vit-base-patch32-b318363')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$helper = Join-Path $PSScriptRoot 'Get-WI0126ClipModel.ps1'
if (-not (Test-Path -LiteralPath $helper)) {
    throw "Pinned CLIP helper not found: $helper"
}

Write-Host 'Preparing the pinned local CLIP assets used by semantic photo search...'
& $helper -OutputDirectory $OutputDirectory
