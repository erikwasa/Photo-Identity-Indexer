[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateRange(1024, 65535)]
    [int]$Port = 5082,
    [switch]$SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = $PSScriptRoot
$artifactRoot = Join-Path $repositoryRoot ".artifacts\launcher-verification"
$publishPath = Join-Path $artifactRoot "app"
$configurationPath = Join-Path $artifactRoot "launcher.json"
$invalidConfigurationPath = Join-Path $artifactRoot "launcher-invalid-geonames-timing.json"
$invalidPrimaryUrlConfigurationPath = Join-Path $artifactRoot "launcher-invalid-primary-url.json"
$invalidMobileHttpConfigurationPath = Join-Path $artifactRoot "launcher-invalid-mobile-http.json"
$invalidMobileWildcardConfigurationPath = Join-Path $artifactRoot "launcher-invalid-mobile-wildcard.json"
$invalidMobileCertificateConfigurationPath = Join-Path $artifactRoot "launcher-invalid-mobile-certificate.json"
$launcherPath = Join-Path $repositoryRoot "Start-PhotoIdentity.ps1"
$databasePath = Join-Path $artifactRoot "catalogue.db"
$analysisPath = Join-Path $artifactRoot "analysis"
$reviewProxyPath = Join-Path $artifactRoot "review-proxies"
$url = "http://127.0.0.1:$Port"

function Get-LauncherServerProcesses {
    $processes = @(Get-CimInstance Win32_Process | Where-Object {
        ($_.Name -ieq "dotnet.exe" -or $_.Name -ieq "PhotoIdentity.Api.exe") -and
        -not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
        $_.CommandLine -like "*PhotoIdentity.Api*" -and
        $_.CommandLine -like "*$Port*"
    })

    return $processes
}

function Invoke-Launcher {
    & powershell.exe `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $launcherPath `
        -ConfigurationPath $configurationPath `
        -NoBrowser `
        -StartupTimeoutSeconds 30

    if ($LASTEXITCODE -ne 0) {
        throw "Launcher exited with code $LASTEXITCODE."
    }
}


function Invoke-LauncherExpectFailure {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedPattern,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $output = @(& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $launcherPath -ConfigurationPath $Path -NoBrowser -StartupTimeoutSeconds 5 2>&1)
    $exitCode = $LASTEXITCODE
    $message = $output -join [Environment]::NewLine

    if ($exitCode -eq 0) {
        throw "Launcher accepted invalid configuration: $Description."
    }
    if ($message -notmatch $ExpectedPattern) {
        throw "Launcher rejected '$Description' without the expected message '$ExpectedPattern'. Output: $message"
    }
    if (@(Get-LauncherServerProcesses).Count -ne 0) {
        throw "Invalid launcher configuration '$Description' started a server before being rejected."
    }
}

function Assert-RejectsUnsafeMobileAccess {
    $primaryRemote = [ordered]@{
        publishPath = $publishPath
        url = "http://0.0.0.0:$Port"
    }
    $primaryRemote | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $invalidPrimaryUrlConfigurationPath -Encoding UTF8
    Invoke-LauncherExpectFailure -Path $invalidPrimaryUrlConfigurationPath -ExpectedPattern "absolute loopback HTTP URL" -Description "remote primary launcher URL"

    $mobileHttp = [ordered]@{
        publishPath = $publishPath
        url = $url
        mobileAccess = [ordered]@{
            enabled = $true
            listenUrl = "http://192.0.2.10:5443"
            certificatePath = "missing.pfx"
        }
    }
    $mobileHttp | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $invalidMobileHttpConfigurationPath -Encoding UTF8
    Invoke-LauncherExpectFailure -Path $invalidMobileHttpConfigurationPath -ExpectedPattern "absolute HTTPS URL" -Description "insecure mobile HTTP listener"

    $mobileWildcard = [ordered]@{
        publishPath = $publishPath
        url = $url
        mobileAccess = [ordered]@{
            enabled = $true
            listenUrl = "https://0.0.0.0:5443"
            certificatePath = "missing.pfx"
        }
    }
    $mobileWildcard | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $invalidMobileWildcardConfigurationPath -Encoding UTF8
    Invoke-LauncherExpectFailure -Path $invalidMobileWildcardConfigurationPath -ExpectedPattern "specific non-loopback IP address" -Description "wildcard mobile HTTPS listener"

    $mobileMissingCertificate = [ordered]@{
        publishPath = $publishPath
        url = $url
        mobileAccess = [ordered]@{
            enabled = $true
            listenUrl = "https://192.0.2.10:5443"
            phoneUrl = "https://photoidentity.invalid:5443"
            certificatePath = "missing-mobile.pfx"
        }
    }
    $mobileMissingCertificate | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $invalidMobileCertificateConfigurationPath -Encoding UTF8
    Invoke-LauncherExpectFailure -Path $invalidMobileCertificateConfigurationPath -ExpectedPattern "certificatePath does not exist" -Description "missing mobile TLS certificate"
}

function Assert-RejectsInvalidGeoNamesTiming {
    $invalidConfiguration = [ordered]@{
        publishPath = $publishPath
        url = $url
        settings = [ordered]@{
            PhotoIdentity__DatabasePath = $databasePath
            PhotoIdentity__GeoNames__AutomaticMinimumRequestIntervalMilliseconds = "-1"
        }
    }
    $invalidConfiguration | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $invalidConfigurationPath -Encoding UTF8

    $output = @(& powershell.exe `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $launcherPath `
        -ConfigurationPath $invalidConfigurationPath `
        -NoBrowser `
        -StartupTimeoutSeconds 5 2>&1)
    $exitCode = $LASTEXITCODE
    $message = $output -join [Environment]::NewLine

    if ($exitCode -eq 0) {
        throw "Launcher accepted a negative GeoNames automatic request interval."
    }
    if ($message -notmatch "between 0 and 600000 milliseconds") {
        throw "Launcher rejected the invalid GeoNames interval without the expected range message. Output: $message"
    }
    if (@(Get-LauncherServerProcesses).Count -ne 0) {
        throw "Invalid GeoNames launcher configuration started a server before being rejected."
    }
}

if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw "Launcher script was not found: $launcherPath"
}

if (-not $SkipPublish) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

    & dotnet publish `
        (Join-Path $repositoryRoot "src\PhotoIdentity.Api\PhotoIdentity.Api.csproj") `
        --configuration $Configuration `
        --no-build `
        --no-restore `
        --output $publishPath

    if ($LASTEXITCODE -ne 0) {
        throw "Launcher verification publish failed with code $LASTEXITCODE."
    }
}
elseif (-not (Test-Path -LiteralPath $publishPath -PathType Container)) {
    throw "-SkipPublish requires existing output at $publishPath."
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

$launcherConfiguration = [ordered]@{
    publishPath = $publishPath
    url = $url
    settings = [ordered]@{
        PhotoIdentity__DatabasePath = $databasePath
        PhotoIdentity__ArchiveAnalysisOutputRoot = $analysisPath
        PhotoIdentity__ReviewProxyRoot = $reviewProxyPath
        PhotoIdentity__GeoNames__Username = "launcher-verification"
        PhotoIdentity__GeoNames__BaseUrl = "https://secure.geonames.org/"
        PhotoIdentity__GeoNames__Language = "en"
        PhotoIdentity__GeoNames__AutomaticEnrichmentEnabled = "false"
        PhotoIdentity__GeoNames__AutomaticMinimumRequestIntervalMilliseconds = "5000"
        PhotoIdentity__GeoNames__AutomaticIdlePollIntervalMilliseconds = "7000"
        PhotoIdentity__RepositoryRoot = $repositoryRoot
    }
}
$launcherConfiguration | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configurationPath -Encoding UTF8

$startedProcessIds = @()
try {
    $preexisting = @(Get-LauncherServerProcesses)
    if ($preexisting.Count -ne 0) {
        throw "Port-specific Photo Identity process already exists before launcher verification: $($preexisting.ProcessId -join ', ')."
    }

    Assert-RejectsInvalidGeoNamesTiming
    Assert-RejectsUnsafeMobileAccess
    Invoke-Launcher

    $firstProcesses = @(Get-LauncherServerProcesses)
    if ($firstProcesses.Count -ne 1) {
        throw "Expected exactly one Photo Identity process after first launch; found $($firstProcesses.Count)."
    }

    $firstProcessId = [int]$firstProcesses[0].ProcessId
    $startedProcessIds = @($firstProcessId)

    $health = Invoke-WebRequest -UseBasicParsing -Uri "$url/health" -TimeoutSec 3
    $payload = $health.Content | ConvertFrom-Json
    if ($health.StatusCode -ne 200 -or [string]$payload.status -ne "ok") {
        throw "Launcher-started application did not return the expected health response."
    }

    $geoNamesStatus = Invoke-RestMethod -Method Get -Uri "$url/api/place-enrichment/status" -TimeoutSec 5
    if ([bool]$geoNamesStatus.automaticEnrichmentEnabled) {
        throw "Launcher GeoNames verification expected automatic enrichment to be disabled for the external-provider-safe test run."
    }
    if ([int]$geoNamesStatus.automaticMinimumRequestIntervalMilliseconds -ne 5000) {
        throw "Launcher did not pass the configured 5000 ms GeoNames automatic request interval to the API without hidden clamping."
    }
    if ([int]$geoNamesStatus.minimumRequestIntervalMilliseconds -ne 5000) {
        throw "GeoNames provider request pacing did not inherit the explicit 5000 ms automatic interval."
    }
    if ([int]$geoNamesStatus.automaticIdlePollIntervalMilliseconds -ne 7000) {
        throw "Launcher did not pass the configured 7000 ms GeoNames automatic idle poll interval to the API."
    }

    Invoke-Launcher

    $secondProcesses = @(Get-LauncherServerProcesses)
    if ($secondProcesses.Count -ne 1) {
        throw "Repeated launch created a conflicting process. Expected one server; found $($secondProcesses.Count)."
    }

    $secondProcessId = [int]$secondProcesses[0].ProcessId
    if ($secondProcessId -ne $firstProcessId) {
        throw "Repeated launch replaced or duplicated the healthy server. Expected PID $firstProcessId; found PID $secondProcessId."
    }

    Write-Host "Windows launcher verification passed. Healthy server PID: $firstProcessId"
}
finally {
    foreach ($processId in $startedProcessIds) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $processId -Timeout 10 -ErrorAction SilentlyContinue
    }

    $remaining = @(Get-LauncherServerProcesses)
    if ($remaining.Count -ne 0) {
        Write-Warning "Launcher verification cleanup still sees Photo Identity process IDs: $($remaining.ProcessId -join ', ')."
        foreach ($remainingProcess in $remaining) {
            Stop-Process -Id ([int]$remainingProcess.ProcessId) -Force -ErrorAction SilentlyContinue
            Wait-Process -Id ([int]$remainingProcess.ProcessId) -Timeout 10 -ErrorAction SilentlyContinue
        }
    }
}