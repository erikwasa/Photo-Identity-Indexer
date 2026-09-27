<#
.SYNOPSIS
Historical entry point for the retired reviewed-catalogue multi-model comparison workflow.

.DESCRIPTION
WI-0149 retired this workflow because it depended on SQLite catalogue backup plus the
legacy batch, match-regenerate and catalogue evaluation-export commands. The path is
kept so completed work-item evidence remains link-valid. It no longer opens a catalogue
or performs model comparison work.
#>
[CmdletBinding()]
param(
    [string] $ConfigPath,
    [switch] $InstallModels,
    [switch] $RunPreflight,
    [switch] $Resume,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($SelfTest) {
    Write-Host "Invoke-MultiModelComparison.ps1 is retired; historical entry-point self-test passed."
    exit 0
}

throw @"
Invoke-MultiModelComparison.ps1 was retired by WI-0149 when the SQLite implementation
and migration-era batch/match/evaluation-export command surface were removed.

Completed comparison evidence remains documented under docs/delivery and
`docs/operations/multi-model-comparison.md`. For current supported commands run:

  dotnet run --project src/PhotoIdentity.Cli -- --help
"@
