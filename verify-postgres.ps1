[CmdletBinding()]
param(
    [string]$EnvironmentPath = (Join-Path $PSScriptRoot "deploy\postgres\.env"),
    [switch]$SkipContainerStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$composeDirectory = Join-Path $PSScriptRoot "deploy\postgres"
$composePath = Join-Path $composeDirectory "compose.yaml"
$environmentExamplePath = Join-Path $composeDirectory ".env.example"

function Read-DotEnv {
    param([Parameter(Mandatory = $true)][string]$Path)

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $separator = $trimmed.IndexOf("=")
        if ($separator -le 0) {
            throw "Invalid .env line in '$Path': $trimmed"
        }

        $name = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        $values[$name] = $value
    }

    return $values
}

if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) {
    throw "PostgreSQL compose definition was not found: $composePath"
}

if (-not (Test-Path -LiteralPath $EnvironmentPath -PathType Leaf)) {
    throw "PostgreSQL private environment file was not found: $EnvironmentPath. Copy '$environmentExamplePath' to '.env' and replace the placeholder password."
}

$settings = Read-DotEnv -Path $EnvironmentPath
foreach ($required in @(
    "PHOTOIDENTITY_POSTGRES_DATABASE",
    "PHOTOIDENTITY_POSTGRES_USER",
    "PHOTOIDENTITY_POSTGRES_PASSWORD",
    "PHOTOIDENTITY_POSTGRES_PORT")) {
    if (-not $settings.ContainsKey($required) -or
        [string]::IsNullOrWhiteSpace([string]$settings[$required])) {
        throw "Required PostgreSQL setting '$required' is missing from $EnvironmentPath."
    }
}

if ([string]$settings["PHOTOIDENTITY_POSTGRES_PASSWORD"] -eq
    "replace-with-a-private-password") {
    throw "Replace the placeholder PostgreSQL password in $EnvironmentPath before starting the service."
}

if (-not $SkipContainerStart) {
    $podman = Get-Command podman -ErrorAction SilentlyContinue
    if ($null -eq $podman) {
        throw "Podman was not found on PATH."
    }

    Push-Location $composeDirectory
    try {
        & $podman.Source compose up -d
        if ($LASTEXITCODE -ne 0) {
            throw "podman compose up failed with code $LASTEXITCODE."
        }

        $containerId = (& $podman.Source compose ps -q postgres).Trim()
        if ([string]::IsNullOrWhiteSpace($containerId)) {
            throw "Podman Compose did not return the PostgreSQL container id."
        }

        $ready = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            & $podman.Source exec $containerId pg_isready -U ([string]$settings["PHOTOIDENTITY_POSTGRES_USER"]) -d ([string]$settings["PHOTOIDENTITY_POSTGRES_DATABASE"]) *> $null
            if ($LASTEXITCODE -eq 0) {
                $ready = $true
                break
            }

            Start-Sleep -Seconds 1
        }

        if (-not $ready) {
            throw "PostgreSQL did not report ready through pg_isready."
        }
    }
    finally {
        Pop-Location
    }
}

$hostConnectionString =
    "Host=127.0.0.1;" +
    "Port=$($settings["PHOTOIDENTITY_POSTGRES_PORT"]);" +
    "Database=$($settings["PHOTOIDENTITY_POSTGRES_DATABASE"]);" +
    "Username=$($settings["PHOTOIDENTITY_POSTGRES_USER"]);" +
    "Password=$($settings["PHOTOIDENTITY_POSTGRES_PASSWORD"]);" +
    "Pooling=false;Timeout=5;Command Timeout=10"

$previous = [Environment]::GetEnvironmentVariable(
    "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING",
    "Process")

try {
    [Environment]::SetEnvironmentVariable(
        "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING",
        $hostConnectionString,
        "Process")

    & dotnet test (Join-Path $PSScriptRoot "tests\PhotoIdentity.Persistence.Tests\PhotoIdentity.Persistence.Tests.csproj") --configuration Release --filter "FullyQualifiedName~PostgresCatalogueDatabaseTests.InitializeAsync_IsVersionedAndIdempotent_WhenLivePostgresIsConfigured"
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL verification test failed with code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING",
        $previous,
        "Process")
}

Write-Host "PostgreSQL runtime verification passed."
