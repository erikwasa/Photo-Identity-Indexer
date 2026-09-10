[CmdletBinding()]
param(
    [string]$DatabasePath,
    [string]$BackupDirectory,
    [string]$EnvironmentPath = (Join-Path $PSScriptRoot "deploy\postgres\.env"),
    [string]$TargetDatabaseName,
    [switch]$ApplicationStopped,
    [switch]$LaunchForReview
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$composeDirectory = Join-Path $PSScriptRoot "deploy\postgres"
$composePath = Join-Path $composeDirectory "compose.yaml"
$cliProject = Join-Path $PSScriptRoot "src\PhotoIdentity.Cli\PhotoIdentity.Cli.csproj"
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

        $name = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        $values[$name] = $value
    }

    return $values
}

function Resolve-ConfiguredPath {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [string]$BaseDirectory
    )

    $expanded = [Environment]::ExpandEnvironmentVariables($Value.Trim())
    if ([IO.Path]::IsPathRooted($expanded)) {
        return [IO.Path]::GetFullPath($expanded)
    }

    if ([string]::IsNullOrWhiteSpace($BaseDirectory)) {
        return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $expanded))
    }

    return [IO.Path]::GetFullPath((Join-Path $BaseDirectory $expanded))
}

function Resolve-LauncherInfo {
    $configurationPath = $null
    foreach ($candidate in @(
        $env:PHOTOIDENTITY_LAUNCHER_CONFIG,
        (Join-Path $PSScriptRoot "PhotoIdentity.launcher.json"),
        $(if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { $null } else { Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher.json" })
    )) {
        if (-not [string]::IsNullOrWhiteSpace([string]$candidate) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            $configurationPath = (Resolve-Path -LiteralPath $candidate).Path
            break
        }
    }

    $defaultRoot = if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        Join-Path $PSScriptRoot ".photoidentity"
    }
    else {
        Join-Path $env:LOCALAPPDATA "PhotoIdentity"
    }
    $resolvedDatabase = Join-Path $defaultRoot "catalogue.db"
    $url = "http://127.0.0.1:5080"

    if ($null -ne $configurationPath) {
        $configurationDirectory = Split-Path -Parent $configurationPath
        $parsed = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
        if ($null -ne $parsed.PSObject.Properties["url"] -and
            -not [string]::IsNullOrWhiteSpace([string]$parsed.url)) {
            $url = ([string]$parsed.url).Trim().TrimEnd('/')
        }

        if ($null -ne $parsed.PSObject.Properties["settings"] -and
            $null -ne $parsed.settings -and
            $null -ne $parsed.settings.PSObject.Properties["PhotoIdentity__DatabasePath"] -and
            -not [string]::IsNullOrWhiteSpace([string]$parsed.settings.PhotoIdentity__DatabasePath)) {
            $resolvedDatabase = Resolve-ConfiguredPath `
                -Value ([string]$parsed.settings.PhotoIdentity__DatabasePath) `
                -BaseDirectory $configurationDirectory
        }
    }

    return [pscustomobject]@{
        ConfigurationPath = $configurationPath
        DatabasePath = [IO.Path]::GetFullPath($resolvedDatabase)
        Url = $url
    }
}

function Get-KnownPhotoIdentityProcessIds {
    $ids = @()
    $ids += @(Get-Process -Name "PhotoIdentity.Api" -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    try {
        $ids += @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
            $_.Name -ieq "dotnet.exe" -and
            -not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
            $_.CommandLine -like "*PhotoIdentity.Api*"
        } | ForEach-Object { [int]$_.ProcessId })
    }
    catch {
        Write-Verbose "Could not inspect dotnet command lines for an additional quiescence check."
    }

    return @($ids | Sort-Object -Unique)
}

function New-TargetConnectionString {
    param(
        [Parameter(Mandatory = $true)]$Settings,
        [Parameter(Mandatory = $true)][string]$DatabaseName
    )

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder["Host"] = "127.0.0.1"
    $builder["Port"] = [string][int]$Settings["PHOTOIDENTITY_POSTGRES_PORT"]
    $builder["Database"] = $DatabaseName
    $builder["Username"] = [string]$Settings["PHOTOIDENTITY_POSTGRES_USER"]
    $builder["Password"] = [string]$Settings["PHOTOIDENTITY_POSTGRES_PASSWORD"]
    $builder["SSL Mode"] = "Disable"
    $builder["GSS Encryption Mode"] = "Disable"
    $builder["Pooling"] = "false"
    $builder["Timeout"] = "5"
    $builder["Command Timeout"] = "30"
    return $builder.ConnectionString
}

function Invoke-Cli {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & dotnet run `
        --project $cliProject `
        --configuration Release `
        --no-build `
        -- @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Photo Identity CLI exited with code $LASTEXITCODE."
    }
}

if (-not $ApplicationStopped) {
    throw "Pass -ApplicationStopped only after stopping Photo Identity. The rehearsal never snapshots a knowingly writable catalogue."
}

$runningProcessIds = @(Get-KnownPhotoIdentityProcessIds)
if ($runningProcessIds.Count -ne 0) {
    throw "Photo Identity still appears to be running (process IDs: $($runningProcessIds -join ', ')). Stop it before rehearsal."
}

if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) {
    throw "PostgreSQL compose definition was not found: $composePath"
}
if (-not (Test-Path -LiteralPath $EnvironmentPath -PathType Leaf)) {
    throw "PostgreSQL private environment file was not found: $EnvironmentPath"
}
if (-not (Test-Path -LiteralPath $cliProject -PathType Leaf)) {
    throw "Photo Identity CLI project was not found: $cliProject"
}

$launcherInfo = Resolve-LauncherInfo
$sourcePath = if ([string]::IsNullOrWhiteSpace($DatabasePath)) {
    $launcherInfo.DatabasePath
}
else {
    Resolve-ConfiguredPath -Value $DatabasePath -BaseDirectory $PSScriptRoot
}
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "SQLite catalogue was not found at '$sourcePath'. Supply -DatabasePath if the launcher uses another catalogue."
}

if ([string]::IsNullOrWhiteSpace($BackupDirectory)) {
    $BackupDirectory = Join-Path (Split-Path -Parent $sourcePath) "migration-backups"
}
$BackupDirectory = Resolve-ConfiguredPath -Value $BackupDirectory -BaseDirectory $PSScriptRoot
New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null

$timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd_HHmmss")
$backupPath = Join-Path $BackupDirectory "catalogue-$timestamp.db"
$reportPath = Join-Path $BackupDirectory "postgres-migration-$timestamp.json"
if ([string]::IsNullOrWhiteSpace($TargetDatabaseName)) {
    $TargetDatabaseName = "photoidentity_rehearsal_${timestamp}_$([Guid]::NewGuid().ToString('N').Substring(0, 8))".ToLowerInvariant()
}
if ($TargetDatabaseName -notmatch '^[a-z][a-z0-9_]{0,62}$') {
    throw "TargetDatabaseName must start with a letter and contain only lowercase letters, digits and underscores (maximum 63 characters)."
}

Write-Host "Building the Release CLI used for backup and migration rehearsal..."
& dotnet build $cliProject --configuration Release | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "CLI Release build failed with code $LASTEXITCODE."
}

Write-Host "Creating a consistent stopped-source SQLite backup..."
Invoke-Cli -Arguments @(
    "catalogue", "backup",
    "--database", $sourcePath,
    "--output", $backupPath,
    "--application-stopped"
)
$backupHashBeforeMigration = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant()

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
if ([string]$settings["PHOTOIDENTITY_POSTGRES_PASSWORD"] -eq "replace-with-a-private-password") {
    throw "Replace the placeholder PostgreSQL password in $EnvironmentPath before rehearsal."
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

$containerId = $null
$targetCreated = $false
$migrationSucceeded = $false
try {
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
            & $podman.Source exec $containerId pg_isready `
                -U ([string]$settings["PHOTOIDENTITY_POSTGRES_USER"]) `
                -d ([string]$settings["PHOTOIDENTITY_POSTGRES_DATABASE"]) *> $null
            if ($LASTEXITCODE -eq 0) {
                $ready = $true
                break
            }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) {
            throw "PostgreSQL did not report ready through pg_isready."
        }

        Write-Host "Creating fresh rehearsal database '$TargetDatabaseName'..."
        & $podman.Source exec `
            -e "TARGET_DB=$TargetDatabaseName" `
            $containerId `
            sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" createdb -h 127.0.0.1 -U "$POSTGRES_USER" "$TARGET_DB"'
        if ($LASTEXITCODE -ne 0) {
            throw "Could not create the fresh rehearsal PostgreSQL database."
        }
        $targetCreated = $true
    }
    finally {
        Pop-Location
    }

    $targetConnectionString = New-TargetConnectionString -Settings $settings -DatabaseName $TargetDatabaseName
    $migrationEnvironmentName = "PHOTOIDENTITY_REHEARSAL_TARGET_CONNECTION_STRING"
    $previousMigrationConnection = [Environment]::GetEnvironmentVariable($migrationEnvironmentName, "Process")
    try {
        [Environment]::SetEnvironmentVariable($migrationEnvironmentName, $targetConnectionString, "Process")
        Write-Host "Migrating the preserved backup into the fresh PostgreSQL rehearsal target..."
        Invoke-Cli -Arguments @(
            "catalogue", "migrate",
            "--sqlite-backup", $backupPath,
            "--postgres-connection-env", $migrationEnvironmentName,
            "--report", $reportPath
        )
    }
    finally {
        [Environment]::SetEnvironmentVariable($migrationEnvironmentName, $previousMigrationConnection, "Process")
    }

    $backupHashAfterMigration = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($backupHashAfterMigration -ne $backupHashBeforeMigration) {
        throw "The preserved SQLite backup changed during migration rehearsal."
    }

    (Get-Item -LiteralPath $backupPath).IsReadOnly = $true
    $migrationSucceeded = $true

    Write-Host ""
    Write-Host "Real-catalogue migration rehearsal passed."
    Write-Host "rehearsal-database: $TargetDatabaseName"
    Write-Host "backup: $backupPath"
    Write-Host "backup-sha256: $backupHashAfterMigration"
    Write-Host "report: $reportPath"
    Write-Host "production-authority-changed: false"

    if ($LaunchForReview) {
        if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
            throw "Launcher was not found: $launcherPath"
        }

        $previousProvider = [Environment]::GetEnvironmentVariable("PhotoIdentity__CatalogueProvider", "Process")
        $previousRuntimeConnection = [Environment]::GetEnvironmentVariable("PhotoIdentity__Postgres__ConnectionString", "Process")
        try {
            [Environment]::SetEnvironmentVariable("PhotoIdentity__CatalogueProvider", "postgresql", "Process")
            [Environment]::SetEnvironmentVariable("PhotoIdentity__Postgres__ConnectionString", $targetConnectionString, "Process")

            $launcherArguments = @(
                "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", $launcherPath
            )
            if ($null -ne $launcherInfo.ConfigurationPath) {
                $launcherArguments += @("-ConfigurationPath", $launcherInfo.ConfigurationPath)
            }

            Write-Host "Starting Photo Identity against the rehearsal PostgreSQL database for read/review acceptance..."
            & powershell.exe @launcherArguments
            if ($LASTEXITCODE -ne 0) {
                throw "Photo Identity launcher failed with code $LASTEXITCODE."
            }

            $health = Invoke-RestMethod -Method Get -Uri "$($launcherInfo.Url)/health" -TimeoutSec 5
            if ([string]$health.status -ne "ok" -or [string]$health.catalogueProvider -ne "postgresql") {
                throw "Rehearsal runtime health did not confirm catalogueProvider=postgresql."
            }
            Write-Host "rehearsal-runtime-health: postgresql"
        }
        finally {
            [Environment]::SetEnvironmentVariable("PhotoIdentity__CatalogueProvider", $previousProvider, "Process")
            [Environment]::SetEnvironmentVariable("PhotoIdentity__Postgres__ConnectionString", $previousRuntimeConnection, "Process")
        }
    }
    else {
        Write-Host "Run this script again only with a new target. For UI review, use -LaunchForReview on the first rehearsal run."
    }
}
finally {
    foreach ($name in $settings.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousComposeEnvironment[$name], "Process")
    }

    if ($targetCreated -and -not $migrationSucceeded -and -not [string]::IsNullOrWhiteSpace($containerId)) {
        Write-Warning "Rehearsal failed. Removing the incomplete PostgreSQL target '$TargetDatabaseName'."
        & $podman.Source exec `
            -e "TARGET_DB=$TargetDatabaseName" `
            $containerId `
            sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" dropdb --if-exists --force -h 127.0.0.1 -U "$POSTGRES_USER" "$TARGET_DB"' *> $null
    }
}
