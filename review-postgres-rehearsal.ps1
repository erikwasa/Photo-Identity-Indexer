[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,
    [string]$EnvironmentPath = (Join-Path $PSScriptRoot "deploy\postgres\.env"),
    [string]$ConfigurationPath,
    [string]$PublishDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$composeDirectory = Join-Path $PSScriptRoot "deploy\postgres"
$composePath = Join-Path $composeDirectory "compose.yaml"
$apiProject = Join-Path $PSScriptRoot "src\PhotoIdentity.Api\PhotoIdentity.Api.csproj"
$launcherPath = Join-Path $PSScriptRoot "Start-PhotoIdentity.ps1"

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
            throw "Invalid .env line in '$Path'."
        }

        $values[$trimmed.Substring(0, $separator).Trim()] = $trimmed.Substring($separator + 1).Trim()
    }

    return $values
}

function Resolve-BaseConfigurationPath {
    if (-not [string]::IsNullOrWhiteSpace($ConfigurationPath)) {
        if (-not (Test-Path -LiteralPath $ConfigurationPath -PathType Leaf)) {
            throw "Launcher configuration does not exist: $ConfigurationPath"
        }
        return (Resolve-Path -LiteralPath $ConfigurationPath).Path
    }

    foreach ($candidate in @(
        $env:PHOTOIDENTITY_LAUNCHER_CONFIG,
        (Join-Path $PSScriptRoot "PhotoIdentity.launcher.json"),
        $(if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { $null } else { Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher.json" })
    )) {
        if (-not [string]::IsNullOrWhiteSpace([string]$candidate) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function New-ReviewConfiguration {
    param(
        [string]$BasePath,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string]$ConnectionEnvironmentName
    )

    if ([string]::IsNullOrWhiteSpace($BasePath)) {
        $configuration = [pscustomobject][ordered]@{
            url = "http://127.0.0.1:5080"
            postgresConnectionEnvironmentVariable = $ConnectionEnvironmentName
            settings = [pscustomobject][ordered]@{
                PhotoIdentity__CatalogueProvider = "postgresql"
            }
            mobileAccess = [pscustomobject][ordered]@{
                enabled = $false
            }
        }
    }
    else {
        $configuration = Get-Content -LiteralPath $BasePath -Raw | ConvertFrom-Json
        Add-Member -InputObject $configuration -MemberType NoteProperty -Name "postgresConnectionEnvironmentVariable" -Value $ConnectionEnvironmentName -Force
        if ($null -eq $configuration.PSObject.Properties["settings"] -or $null -eq $configuration.settings) {
            Add-Member -InputObject $configuration -MemberType NoteProperty -Name "settings" -Value ([pscustomobject]@{}) -Force
        }
        Add-Member -InputObject $configuration.settings -MemberType NoteProperty -Name "PhotoIdentity__CatalogueProvider" -Value "postgresql" -Force
        Add-Member -InputObject $configuration -MemberType NoteProperty -Name "mobileAccess" -Value ([pscustomobject][ordered]@{
            enabled = $false
        }) -Force
    }

    $configuration | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    return $configuration
}

if ($DatabaseName -notmatch '^[a-z][a-z0-9_]{0,62}$') {
    throw "DatabaseName must start with a letter and contain only lowercase letters, digits and underscores (maximum 63 characters)."
}
if (-not (Test-Path -LiteralPath $EnvironmentPath -PathType Leaf)) {
    throw "PostgreSQL private environment file was not found: $EnvironmentPath"
}
if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) {
    throw "PostgreSQL compose definition was not found: $composePath"
}
if (-not (Test-Path -LiteralPath $apiProject -PathType Leaf)) {
    throw "Photo Identity API project was not found: $apiProject"
}
if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw "Photo Identity launcher was not found: $launcherPath"
}

$settings = Read-DotEnv -Path $EnvironmentPath
foreach ($required in @("PHOTOIDENTITY_POSTGRES_DATABASE", "PHOTOIDENTITY_POSTGRES_USER", "PHOTOIDENTITY_POSTGRES_PASSWORD", "PHOTOIDENTITY_POSTGRES_PORT")) {
    if (-not $settings.ContainsKey($required) -or [string]::IsNullOrWhiteSpace([string]$settings[$required])) {
        throw "Required PostgreSQL setting '$required' is missing from $EnvironmentPath."
    }
}

$podman = Get-Command podman -ErrorAction SilentlyContinue
if ($null -eq $podman) {
    throw "Podman was not found on PATH."
}

$previousComposeEnvironment = @{}
foreach ($name in $settings.Keys) {
    $previousComposeEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
    [Environment]::SetEnvironmentVariable($name, [string]$settings[$name], "Process")
}

try {
    Push-Location $composeDirectory
    try {
        & $podman.Source compose up -d
        if ($LASTEXITCODE -ne 0) {
            throw "podman compose up failed with code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder["Host"] = "127.0.0.1"
    $builder["Port"] = [string][int]$settings["PHOTOIDENTITY_POSTGRES_PORT"]
    $builder["Database"] = $DatabaseName
    $builder["Username"] = [string]$settings["PHOTOIDENTITY_POSTGRES_USER"]
    $builder["Password"] = [string]$settings["PHOTOIDENTITY_POSTGRES_PASSWORD"]
    $builder["SSL Mode"] = "Disable"
    $builder["GSS Encryption Mode"] = "Disable"
    $builder["Pooling"] = "false"
    $builder["Timeout"] = "5"
    $builder["Command Timeout"] = "30"
    $connectionString = $builder.ConnectionString

    $reviewRoot = if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
        Join-Path ([IO.Path]::GetTempPath()) "PhotoIdentity\postgres-rehearsal-review\$DatabaseName"
    }
    else {
        [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($PublishDirectory))
    }
    $publishPath = Join-Path $reviewRoot "app"
    $temporaryConfigurationPath = Join-Path $reviewRoot "launcher.json"
    New-Item -ItemType Directory -Path $reviewRoot -Force | Out-Null

    Write-Host "Publishing the current Photo Identity API for rehearsal review..."
    & dotnet publish $apiProject --configuration Release --output $publishPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Photo Identity API rehearsal publish failed with code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath "PhotoIdentity.Api.dll") -PathType Leaf)) {
        throw "Rehearsal publish did not produce PhotoIdentity.Api.dll at '$publishPath'."
    }

    $baseConfigurationPath = Resolve-BaseConfigurationPath
    $runtimeEnvironmentName = "PHOTOIDENTITY_REHEARSAL_RUNTIME_CONNECTION_STRING"
    $configuration = New-ReviewConfiguration -BasePath $baseConfigurationPath -OutputPath $temporaryConfigurationPath -ConnectionEnvironmentName $runtimeEnvironmentName
    $url = if ($null -ne $configuration.PSObject.Properties["url"] -and -not [string]::IsNullOrWhiteSpace([string]$configuration.url)) {
        ([string]$configuration.url).Trim().TrimEnd('/')
    }
    else {
        "http://127.0.0.1:5080"
    }

    $previousRuntimeConnection = [Environment]::GetEnvironmentVariable($runtimeEnvironmentName, "Process")
    try {
        [Environment]::SetEnvironmentVariable($runtimeEnvironmentName, $connectionString, "Process")
        Write-Host "Starting Photo Identity against rehearsal database '$DatabaseName'..."
        & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $launcherPath -ConfigurationPath $temporaryConfigurationPath -PublishPathOverride $publishPath
        if ($LASTEXITCODE -ne 0) {
            throw "Photo Identity launcher failed with code $LASTEXITCODE."
        }

        $health = Invoke-RestMethod -Method Get -Uri "$url/health" -TimeoutSec 5
        if ([string]$health.status -ne "ok" -or [string]$health.catalogueProvider -ne "postgresql") {
            throw "Rehearsal runtime health did not confirm catalogueProvider=postgresql."
        }

        Write-Host "rehearsal-database: $DatabaseName"
        Write-Host "rehearsal-launcher-config: $temporaryConfigurationPath"
        Write-Host "rehearsal-publish-path: $publishPath"
        Write-Host "rehearsal-runtime-health: postgresql"
    }
    finally {
        [Environment]::SetEnvironmentVariable($runtimeEnvironmentName, $previousRuntimeConnection, "Process")
    }
}
finally {
    foreach ($name in $settings.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousComposeEnvironment[$name], "Process")
    }
}
