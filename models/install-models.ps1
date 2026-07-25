[CmdletBinding()]
param(
    [string[]] $Id = @(),
    [string] $ModelDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$arguments = @(
    'run',
    '--project',
    (Join-Path $repositoryRoot 'tools/PhotoIdentity.Models'),
    '--',
    'install',
    '--root',
    $repositoryRoot
)

if ($ModelDirectory) {
    $arguments += @('--model-dir', $ModelDirectory)
}

foreach ($modelId in $Id) {
    $arguments += @('--id', $modelId)
}

& dotnet @arguments
exit $LASTEXITCODE
