[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $IsWindows) {
    Write-Host "SKIP: packaged launcher retry smoke check requires Windows cmd.exe."
    exit 0
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$sourceEntryPoint = Join-Path $repositoryRoot "packaging\windows\PhotoIdentity.cmd"
if (-not (Test-Path -LiteralPath $sourceEntryPoint -PathType Leaf)) {
    throw "Packaged entry point was not found: $sourceEntryPoint"
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("photoidentity-package-startup-retry-" + [Guid]::NewGuid().ToString("N"))
$packageRoot = Join-Path $tempRoot "package"
$localAppData = Join-Path $tempRoot "localappdata"
$attemptPath = Join-Path $packageRoot "attempt-count.txt"
$stderrPath = Join-Path $localAppData "PhotoIdentity\launcher-logs\api.stderr.log"
$fakeLauncherPath = Join-Path $packageRoot "Start-PhotoIdentity.ps1"
$entryPoint = Join-Path $packageRoot "PhotoIdentity.cmd"

$previousLocalAppData = $env:LOCALAPPDATA
$previousNonInteractive = $env:PHOTOIDENTITY_NONINTERACTIVE
$previousScenario = $env:PHOTOIDENTITY_TEST_STARTUP_SCENARIO

try {
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $packageRoot "app") -Force | Out-Null
    New-Item -ItemType Directory -Path $localAppData -Force | Out-Null
    Copy-Item -LiteralPath $sourceEntryPoint -Destination $entryPoint -Force

    @'
[CmdletBinding()]
param(
    [string]$PublishPathOverride,
    [switch]$NoBrowser,
    [int]$StartupTimeoutSeconds = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$attemptPath = Join-Path $PSScriptRoot "attempt-count.txt"
$attempt = 0
if (Test-Path -LiteralPath $attemptPath -PathType Leaf) {
    $attempt = [int](Get-Content -LiteralPath $attemptPath -Raw)
}
$attempt++
Set-Content -LiteralPath $attemptPath -Value $attempt -Encoding ASCII

$stderrPath = Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher-logs\api.stderr.log"
New-Item -ItemType Directory -Path (Split-Path -Parent $stderrPath) -Force | Out-Null

switch ($env:PHOTOIDENTITY_TEST_STARTUP_SCENARIO) {
    "transient" {
        if ($attempt -eq 1) {
            Set-Content -LiteralPath $stderrPath -Encoding ASCII -Value @(
                "Exception data:",
                "  SqlState: 57P03",
                "  MessageText: the database system is starting up"
            )
            exit 1
        }
        Set-Content -LiteralPath $stderrPath -Encoding ASCII -Value ""
        exit 0
    }
    "nontransient" {
        Set-Content -LiteralPath $stderrPath -Encoding ASCII -Value @(
            "Exception data:",
            "  SqlState: 28P01",
            "  MessageText: password authentication failed"
        )
        exit 1
    }
    default {
        throw "Unknown test scenario '$($env:PHOTOIDENTITY_TEST_STARTUP_SCENARIO)'."
    }
}
'@ | Set-Content -LiteralPath $fakeLauncherPath -Encoding UTF8

    $env:LOCALAPPDATA = $localAppData
    $env:PHOTOIDENTITY_NONINTERACTIVE = "1"

    $env:PHOTOIDENTITY_TEST_STARTUP_SCENARIO = "transient"
    Remove-Item -LiteralPath $attemptPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    & $entryPoint -NoBrowser -StartupTimeoutSeconds 1
    if ($LASTEXITCODE -ne 0) {
        throw "Expected transient PostgreSQL startup to recover, but PhotoIdentity.cmd exited with $LASTEXITCODE."
    }
    $transientAttempts = [int](Get-Content -LiteralPath $attemptPath -Raw)
    if ($transientAttempts -ne 2) {
        throw "Expected exactly two startup attempts for the transient scenario; observed $transientAttempts."
    }

    $env:PHOTOIDENTITY_TEST_STARTUP_SCENARIO = "nontransient"
    Remove-Item -LiteralPath $attemptPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    & $entryPoint -NoBrowser -StartupTimeoutSeconds 1
    if ($LASTEXITCODE -eq 0) {
        throw "Expected non-transient PostgreSQL startup failure to propagate."
    }
    $nonTransientAttempts = [int](Get-Content -LiteralPath $attemptPath -Raw)
    if ($nonTransientAttempts -ne 1) {
        throw "Expected one startup attempt for the non-transient scenario; observed $nonTransientAttempts."
    }

    Write-Host "PASS: packaged launcher retries PostgreSQL 57P03 once and does not retry non-transient failures."
}
finally {
    $env:LOCALAPPDATA = $previousLocalAppData
    $env:PHOTOIDENTITY_NONINTERACTIVE = $previousNonInteractive
    $env:PHOTOIDENTITY_TEST_STARTUP_SCENARIO = $previousScenario
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
