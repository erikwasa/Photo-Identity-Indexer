[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot,
    [string]$PackageVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot ".artifacts\packages"
}
else {
    $OutputRoot = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($OutputRoot))
}

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) {
        $PackageVersion = $env:GITHUB_SHA.Substring(0, [Math]::Min(12, $env:GITHUB_SHA.Length))
    }
    else {
        $git = Get-Command git -ErrorAction SilentlyContinue
        if ($null -ne $git) {
            $candidate = (& $git.Source -C $repositoryRoot rev-parse --short=12 HEAD 2>$null)
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($candidate)) {
                $PackageVersion = $candidate.Trim()
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
        $PackageVersion = "local"
    }
}

$packageName = "PhotoIdentity-$RuntimeIdentifier"
$packageRoot = Join-Path $OutputRoot $packageName
$appRoot = Join-Path $packageRoot "app"
$zipPath = Join-Path $OutputRoot "$packageName.zip"
$apiProject = Join-Path $repositoryRoot "src\PhotoIdentity.Api\PhotoIdentity.Api.csproj"
$launcherScript = Join-Path $repositoryRoot "Start-PhotoIdentity.ps1"
$packageEntryPoint = Join-Path $repositoryRoot "packaging\windows\PhotoIdentity.cmd"
$packageReadme = Join-Path $repositoryRoot "packaging\windows\README.txt"
$packageConfigurationExample = Join-Path $repositoryRoot "packaging\windows\PhotoIdentity.launcher.example.json"

foreach ($requiredPath in @($apiProject, $launcherScript, $packageEntryPoint, $packageReadme, $packageConfigurationExample)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required packaging input was not found: $requiredPath"
    }
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $appRoot -Force | Out-Null

Write-Host "Publishing Photo Identity for $RuntimeIdentifier as a self-contained application..."
& dotnet publish `
    $apiProject `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $appRoot

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExecutable = Join-Path $appRoot "PhotoIdentity.Api.exe"
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Self-contained publish did not produce PhotoIdentity.Api.exe."
}

Copy-Item -LiteralPath $launcherScript -Destination (Join-Path $packageRoot "Start-PhotoIdentity.ps1") -Force
Copy-Item -LiteralPath $packageEntryPoint -Destination (Join-Path $packageRoot "PhotoIdentity.cmd") -Force
Copy-Item -LiteralPath $packageReadme -Destination (Join-Path $packageRoot "README.txt") -Force
Copy-Item -LiteralPath $packageConfigurationExample -Destination (Join-Path $packageRoot "PhotoIdentity.launcher.example.json") -Force

$manifest = [ordered]@{
    schemaVersion = 1
    packageVersion = $PackageVersion
    runtimeIdentifier = $RuntimeIdentifier
    deploymentMode = "self-contained"
    entryPoint = "PhotoIdentity.cmd"
    applicationExecutable = "app/PhotoIdentity.Api.exe"
    durableApplicationData = "%LOCALAPPDATA%/PhotoIdentity"
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $packageRoot "package-manifest.json") -Encoding UTF8

$privateFiles = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object {
    $_.Name -ieq "PhotoIdentity.launcher.json" -or
    $_.Extension -ieq ".db" -or
    $_.Extension -ieq ".sqlite" -or
    $_.Extension -ieq ".sqlite3"
})
if ($privateFiles.Count -ne 0) {
    throw "Package unexpectedly contains private/durable data candidates: $($privateFiles.FullName -join ', ')"
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force

$packageBytes = (Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum
$zipBytes = (Get-Item -LiteralPath $zipPath).Length
$packageMegabytes = [Math]::Round($packageBytes / 1MB, 1)
$zipMegabytes = [Math]::Round($zipBytes / 1MB, 1)

Write-Host "Windows package created: $zipPath"
Write-Host "Uncompressed size: $packageMegabytes MB"
Write-Host "ZIP size: $zipMegabytes MB"
Write-Host "Deployment mode: self-contained ($RuntimeIdentifier)"

[pscustomobject]@{
    PackageRoot = $packageRoot
    ZipPath = $zipPath
    RuntimeIdentifier = $RuntimeIdentifier
    DeploymentMode = "self-contained"
    PackageVersion = $PackageVersion
    UncompressedBytes = $packageBytes
    ZipBytes = $zipBytes
}
