[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$solution = Join-Path $PSScriptRoot "PhotoIdentity.slnx"

dotnet restore $solution
dotnet test $solution --configuration $Configuration --no-restore
