<#
.SYNOPSIS
Restores and tests the solution.

.EXAMPLE
./test.ps1

.EXAMPLE
./test.ps1 -Configuration Debug
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

dotnet test $solution --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}
