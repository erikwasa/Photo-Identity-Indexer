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
$archiveRoot = Join-Path $artifactRoot "archive-source"
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

function Initialize-ArchiveFixture {
    param([Parameter(Mandatory = $true)][string]$SourceRoot)

    New-Item -ItemType Directory -Path $SourceRoot -Force | Out-Null
    [IO.File]::WriteAllBytes(
        (Join-Path $SourceRoot "package-analysis.jpg"),
        [Convert]::FromBase64String(
            "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgr/wAARCAAKABQDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD8V6KKK8M9gKKKKAP/2Q=="))
}

function Assert-PackagedArchiveProfileReady {
    param([Parameter(Mandatory = $true)][string]$SourceRoot)

    $request = [ordered]@{
        rootPath = $SourceRoot
        relativeFolder = "."
    } | ConvertTo-Json

    $status = Invoke-RestMethod `
        -Method Post `
        -Uri "$url/api/archive/include" `
        -ContentType "application/json" `
        -Body $request `
        -TimeoutSec 10

    if (-not [bool]$status.analysisReady) {
        throw "Packaged archive profile did not resolve without a source-checkout RepositoryRoot. Message: $($status.analysisMessage)"
    }
    if ([string]::IsNullOrWhiteSpace([string]$status.profileHash)) {
        throw "Packaged archive profile resolved without a profile hash."
    }
}

function Assert-PackagedArchiveAnalysis {
    $sync = Invoke-RestMethod -Method Post -Uri "$url/api/archive/sync" -TimeoutSec 30
    if ([int]$sync.supportedFiles -lt 1) {
        throw "Packaged archive verification did not discover its local image fixture."
    }

    $step = Invoke-RestMethod -Method Post -Uri "$url/api/archive/analysis/step" -TimeoutSec 60
    if (-not [bool]$step.status.analysisReady) {
        throw "Packaged archive analysis became unavailable: $($step.status.analysisMessage)"
    }
    if ([int]$step.status.totals.failedImages -ne 0) {
        throw "Packaged archive analysis recorded a failed image."
    }
    if ([int]$step.status.totals.analysedImages -lt 1) {
        throw "Packaged archive analysis did not complete the local image fixture."
    }
}

function Assert-PackagedStoragePolicy {
    $storage = Invoke-RestMethod -Method Get -Uri "$url/api/archive/storage" -TimeoutSec 10
    if (-not [bool]$storage.policyConfigured) {
        throw "Packaged hydration policy was not configured. Message: $($storage.policyMessage)"
    }
    if ([long]$storage.minimumFreeSpaceReserveBytes -ne 21474836480L) {
        throw "Packaged minimum free-space reserve did not match the launcher policy."
    }
    if ([long]$storage.maximumManagedHydrationBytes -ne 10737418240L) {
        throw "Packaged managed-hydration budget did not match the launcher policy."
    }
    if ([int]$storage.maximumConcurrentOperations -ne 2) {
        throw "Packaged hydration concurrency did not match the launcher policy."
    }
    if ($null -eq $storage.availableFreeBytes) {
        throw "Packaged storage status did not expose available free space."
    }
}

if (-not (Test-Path -LiteralPath $packageScript -PathType Leaf)) {
    throw "Package script was not found: $packageScript"
}

Remove-Item -LiteralPath $artifactRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
New-Item -ItemType Directory -Path $localAppData -Force | Out-Null
Initialize-ArchiveFixture -SourceRoot $archiveRoot

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
    if ([string]$manifest.analysisManifestDirectory -ne "app/models/manifests") {
        throw "Package manifest does not identify the packaged archive-analysis manifests."
    }
    if ([string]$manifest.analysisModelDirectory -ne "app/models/files") {
        throw "Package manifest does not identify the packaged archive-analysis model files."
    }

    if (-not (Test-Path -LiteralPath (Join-Path $installV1 "app\PhotoIdentity.Api.exe") -PathType Leaf)) {
        throw "Package does not contain the self-contained PhotoIdentity.Api.exe host."
    }

    foreach ($relativeManifest in @(
        "app\models\manifests\centerface-2019-fp32.json",
        "app\models\manifests\sface-2021dec-fp32.json")) {
        $packagedManifestPath = Join-Path $installV1 $relativeManifest
        if (-not (Test-Path -LiteralPath $packagedManifestPath -PathType Leaf)) {
            throw "Package is missing required archive-analysis manifest: $relativeManifest"
        }

        $modelManifest = Get-Content -LiteralPath $packagedManifestPath -Raw | ConvertFrom-Json
        $packagedModelPath = Join-Path $installV1 ("app\models\files\" + [string]$modelManifest.fileName)
        if (-not (Test-Path -LiteralPath $packagedModelPath -PathType Leaf)) {
            throw "Package is missing governed model file for $($modelManifest.modelId): $($modelManifest.fileName)"
        }
        if ((Get-Item -LiteralPath $packagedModelPath).Length -ne [long]$modelManifest.sizeBytes) {
            throw "Packaged model size did not match the governed manifest for $($modelManifest.modelId)."
        }
        $actualHash = (Get-FileHash -LiteralPath $packagedModelPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne ([string]$modelManifest.sha256).ToLowerInvariant()) {
            throw "Packaged model SHA-256 did not match the governed manifest for $($modelManifest.modelId)."
        }
    }

    $launcherExamplePath = Join-Path $installV1 "PhotoIdentity.launcher.example.json"
    if (-not (Test-Path -LiteralPath $launcherExamplePath -PathType Leaf)) {
        throw "Package launcher configuration example is missing."
    }
    $launcherExample = Get-Content -LiteralPath $launcherExamplePath -Raw | ConvertFrom-Json
    if ([string]$launcherExample.url -ne "http://127.0.0.1:5080") {
        throw "Package launcher example must preserve the default loopback HTTP URL."
    }
    if ($null -eq $launcherExample.PSObject.Properties["mobileAccess"] -or
        $null -eq $launcherExample.mobileAccess -or
        [bool]$launcherExample.mobileAccess.enabled) {
        throw "Package launcher example must keep trusted-LAN mobile access explicitly disabled by default."
    }

    $exampleSettingNames = @($launcherExample.settings.PSObject.Properties.Name)
    foreach ($requiredSetting in @(
        "PhotoIdentity__ArchiveHydration__MinimumFreeSpaceReserveBytes",
        "PhotoIdentity__ArchiveHydration__MaximumManagedHydrationBytes",
        "PhotoIdentity__ArchiveHydration__MaximumConcurrentOperations")) {
        if ($exampleSettingNames -notcontains $requiredSetting) {
            throw "Package launcher configuration example is missing bounded-storage setting: $requiredSetting"
        }
    }
    if ([string]$launcherExample.settings.PhotoIdentity__ReviewProxyProfileId -ne "jpeg-1600-q78" -or
        [string]$launcherExample.settings.PhotoIdentity__ReviewProxyMaximumLongEdge -ne "1600" -or
        [string]$launcherExample.settings.PhotoIdentity__ReviewProxyJpegQuality -ne "78") {
        throw "Package launcher example does not contain the selected review-proxy profile settings."
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
            PhotoIdentity__ReviewProxyProfileId = "jpeg-1600-q78"
            PhotoIdentity__ReviewProxyMaximumLongEdge = "1600"
            PhotoIdentity__ReviewProxyJpegQuality = "78"
            PhotoIdentity__ArchiveHydration__MinimumFreeSpaceReserveBytes = "21474836480"
            PhotoIdentity__ArchiveHydration__MaximumManagedHydrationBytes = "10737418240"
            PhotoIdentity__ArchiveHydration__MaximumConcurrentOperations = "2"
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
    Assert-PackagedArchiveProfileReady -SourceRoot $archiveRoot
    Assert-PackagedStoragePolicy
    Assert-PackagedArchiveAnalysis

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