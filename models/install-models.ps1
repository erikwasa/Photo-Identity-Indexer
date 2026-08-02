<#
.SYNOPSIS
Installs and verifies pinned model files from checked-in manifests.

.EXAMPLE
./models/install-models.ps1

.EXAMPLE
./models/install-models.ps1 -Id sface-2021dec-int8
#>
[CmdletBinding()]
param(
    [string[]] $Id = @(),
    [string] $ModelDirectory,

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$arguments = @(
    "run",
    "--project",
    (Join-Path $repositoryRoot "tools/PhotoIdentity.Models"),
    "--configuration",
    $Configuration,
    "--",
    "install",
    "--root",
    $repositoryRoot
)

if ($ModelDirectory) {
    $arguments += @("--model-dir", $ModelDirectory)
}

foreach ($modelId in $Id) {
    $arguments += @("--id", $modelId)
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Model installation failed with exit code $LASTEXITCODE."
}
