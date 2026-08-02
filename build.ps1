<#
.SYNOPSIS
Restores and builds the solution.

.EXAMPLE
./build.ps1

.EXAMPLE
./build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$solution = Join-Path $PSScriptRoot "PhotoIdentity.slnx"

dotnet restore $solution
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

dotnet build $solution --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}
