<#
.SYNOPSIS
Restores and tests the solution.

.EXAMPLE
./test.ps1

.EXAMPLE
./test.ps1 -Configuration Debug

.EXAMPLE
./test.ps1 -Scope Fast

.DESCRIPTION
Full (default) tests the whole solution; Fast runs the same non-integration
test filter used by CI for focused local feedback. CI remains the full
automated acceptance gate.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [ValidateSet("Full", "Fast")]
    [string] $Scope = "Full"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$solution = Join-Path $PSScriptRoot "PhotoIdentity.slnx"

dotnet restore $solution
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

$testArguments = @($solution, "--configuration", $Configuration, "--no-restore")
if ($Scope -eq "Fast") {
    $testArguments += @(
        "--filter",
        "FullyQualifiedName!~PhotoIdentity_Integration_Tests&FullyQualifiedName!~PhotoIdentity.Integration.Tests"
    )
}
dotnet test @testArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}
