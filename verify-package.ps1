[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateRange(1024, 65535)]
    [int]$Port = 5083
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = $PSScriptRoot
$artifactRoot = Join-Path $repositoryRoot ".artifacts\package-verification"
$packageOutputRoot = Join-Path $artifactRoot "packages"
$installV1 = Join-Path $artifactRoot "install-v1"
$installV2 = Join-Path $artifactRoot "install-v2"
$localAppData = Join-Path $artifactRoot "localappdata"
$packageScript = Join-Path $repositoryRoot "Package-PhotoIdentity.ps1"
$packageZip = Join-Path $packageOutputRoot "PhotoIdentity-win-x64.zip"
$url = "http://127.0.0.1:$Port"

function Get-PackageServerProcesses {
    $processes = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -ieq "PhotoIdentity.Api.exe" -and
        -not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
        $_.CommandLine -like "*$Port*"
    })

    return $processes
}

function Stop-PackageServerProcesses {
    param([int[]]$ProcessIds)

    foreach ($processId in $ProcessIds) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remaining = @($ProcessIds | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        if ($remaining.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 150
    }

    $remaining = @($ProcessIds | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    if ($remaining.Count -ne 0) {
        throw "Package verification could not stop process(es): $($remaining -join ', ')."
    }
}

function Invoke-PackageEntryPoint {
    param([Parameter(Mandatory = $true)][string]$InstallRoot)

    $entryPoint = Join-Path $InstallRoot "PhotoIdentity.cmd"
    if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
        throw "Packaged entry point was not found: $entryPoint"
    }

    & $entryPoint -NoBrowser -StartupTimeoutSeconds 30
    if ($LASTEXITCODE -ne 0) {
        throw "Packaged entry point exited with code $LASTEXITCODE."
    }
}

function Assert-Healthy {
    $response = Invoke-WebRequest -UseBasicParsing -Uri "$url/health" -TimeoutSec 3
    $payload = $response.Content | ConvertFrom-Json
    if ($response.StatusCode -ne 200 -or [string]$payload.status -ne "ok") {
        throw "Packaged application did not return the expected health response."
    }
}

if (-not (Test-Path -LiteralPath $packageScript -PathType Leaf)) {
    throw "Package script was not found: $packageScript"
}

Remove-Item -LiteralPath $artifactRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
New-Item -ItemType Directory -Path $localAppData -Force | Out-Null

$previousLocalAppData = $env:LOCALAPPDATA
$previousNonInteractive = $env:PHOTOIDENTITY_NONINTERACTIVE
$startedProcessIds = @()
try {
    $env:LOCALAPPDATA = $localAppData
    $env:PHOTOIDENTITY_NONINTERACTIVE = "1"

    & $packageScript -Configuration $Configuration -RuntimeIdentifier win-x64 -OutputRoot $packageOutputRoot -PackageVersion "verification"
    if ($LASTEXITCODE -ne 0) {
        throw "Package build failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $packageZip -PathType Leaf)) {
        throw "Expected package ZIP was not produced: $packageZip"
    }

    Expand-Archive -LiteralPath $packageZip -DestinationPath $installV1 -Force

    $manifestPath = Join-Path $installV1 "package-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Package manifest is missing."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([string]$manifest.runtimeIdentifier -ne "win-x64" -or [string]$manifest.deploymentMode -ne "self-contained") {
        throw "Package manifest does not describe the expected self-contained win-x64 deployment."
    }

    if (-not (Test-Path -LiteralPath (Join-Path $installV1 "app\PhotoIdentity.Api.exe") -PathType Leaf)) {
        throw "Package does not contain the self-contained PhotoIdentity.Api.exe host."
    }

    $embeddedPrivateFiles = @(Get-ChildItem -LiteralPath $installV1 -Recurse -File | Where-Object {
        $_.Name -ieq "PhotoIdentity.launcher.json" -or
        $_.Extension -ieq ".db" -or
        $_.Extension -ieq ".sqlite" -or
        $_.Extension -ieq ".sqlite3"
    })
    if ($embeddedPrivateFiles.Count -ne 0) {
        throw "Extracted package contains durable/private files: $($embeddedPrivateFiles.FullName -join ', ')."
    }

    $configurationDirectory = Join-Path $localAppData "PhotoIdentity"
    New-Item -ItemType Directory -Path $configurationDirectory -Force | Out-Null
    $configurationPath = Join-Path $configurationDirectory "launcher.json"
    $databasePath = Join-Path $configurationDirectory "catalogue.db"
    $analysisPath = Join-Path $configurationDirectory "archive-analysis"
    $reviewProxyPath = Join-Path $configurationDirectory "review-proxies"
    $launcherConfiguration = [ordered]@{
        url = $url
        settings = [ordered]@{
            PhotoIdentity__DatabasePath = $databasePath
            PhotoIdentity__ArchiveAnalysisOutputRoot = $analysisPath
            PhotoIdentity__ReviewProxyRoot = $reviewProxyPath
        }
    }
    $launcherConfiguration | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configurationPath -Encoding UTF8
    $configurationHash = (Get-FileHash -LiteralPath $configurationPath -Algorithm SHA256).Hash

    $preexisting = @(Get-PackageServerProcesses)
    if ($preexisting.Count -ne 0) {
        throw "Package-specific Photo Identity process already exists before verification: $($preexisting.ProcessId -join ', ')."
    }

    Invoke-PackageEntryPoint -InstallRoot $installV1
    Assert-Healthy

    $firstProcesses = @(Get-PackageServerProcesses)
    if ($firstProcesses.Count -ne 1) {
        throw "Expected one packaged Photo Identity process after first launch; found $($firstProcesses.Count)."
    }

    $firstProcessId = [int]$firstProcesses[0].ProcessId
    $startedProcessIds = @($firstProcessId)

    if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) {
        throw "Packaged application did not create the catalogue in durable local application data."
    }

    if (Test-Path -LiteralPath (Join-Path $installV1 "catalogue.db") -PathType Leaf) {
        throw "Packaged application wrote the catalogue into the replaceable package directory."
    }

    Invoke-PackageEntryPoint -InstallRoot $installV1
    $secondProcesses = @(Get-PackageServerProcesses)
    if ($secondProcesses.Count -ne 1 -or [int]$secondProcesses[0].ProcessId -ne $firstProcessId) {
        throw "Repeated packaged launch did not reuse the same healthy server instance."
    }

    $markerPath = Join-Path $configurationDirectory "upgrade-preservation.marker"
    Set-Content -LiteralPath $markerPath -Value "preserve-me" -Encoding UTF8

    Stop-PackageServerProcesses -ProcessIds @($firstProcessId)
    $startedProcessIds = @()

    Expand-Archive -LiteralPath $packageZip -DestinationPath $installV2 -Force
    Invoke-PackageEntryPoint -InstallRoot $installV2
    Assert-Healthy

    $upgradeProcesses = @(Get-PackageServerProcesses)
    if ($upgradeProcesses.Count -ne 1) {
        throw "Expected one packaged Photo Identity process after side-by-side upgrade launch; found $($upgradeProcesses.Count)."
    }
    $startedProcessIds = @([int]$upgradeProcesses[0].ProcessId)

    if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) {
        throw "Catalogue was not preserved across package replacement."
    }
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "Durable local application data was not preserved across package replacement."
    }
    if ((Get-FileHash -LiteralPath $configurationPath -Algorithm SHA256).Hash -ne $configurationHash) {
        throw "Launcher configuration changed during package replacement."
    }

    Write-Host "Windows package verification passed."
    Write-Host "Package: $packageZip"
    Write-Host "Durable data root: $configurationDirectory"
}
finally {
    $cleanupProcessIds = @($startedProcessIds)
    try {
        $cleanupProcessIds += @(Get-PackageServerProcesses | ForEach-Object { [int]$_.ProcessId })
    }
    catch {
        Write-Warning "Could not enumerate package verification processes during cleanup: $($_.Exception.Message)"
    }

    $cleanupProcessIds = @($cleanupProcessIds | Sort-Object -Unique)
    if ($cleanupProcessIds.Count -ne 0) {
        Stop-PackageServerProcesses -ProcessIds $cleanupProcessIds
    }

    $env:LOCALAPPDATA = $previousLocalAppData
    $env:PHOTOIDENTITY_NONINTERACTIVE = $previousNonInteractive
}
