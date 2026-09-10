[CmdletBinding()]
param(
    [string]$DatabasePath,
    [string]$BackupDirectory,
    [string]$EnvironmentPath = (Join-Path $PSScriptRoot "deploy\postgres\.env"),
    [string]$TargetDatabaseName,
    [string]$SecondaryTargetDatabaseName,
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

        $values[$trimmed.Substring(0, $separator).Trim()] = $trimmed.Substring($separator + 1).Trim()
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

    $base = if ([string]::IsNullOrWhiteSpace($BaseDirectory)) { $PSScriptRoot } else { $BaseDirectory }
    return [IO.Path]::GetFullPath((Join-Path $base $expanded))
}

function Resolve-LauncherInfo {
    $configurationPath = $null
    foreach ($candidate in @(
        $env:PHOTOIDENTITY_LAUNCHER_CONFIG,
        (Join-Path $PSScriptRoot "PhotoIdentity.launcher.json"),
        $(if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { $null } else { Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher.json" })
    )) {
        if (-not [string]::IsNullOrWhiteSpace([string]$candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
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
        if ($null -ne $parsed.PSObject.Properties["url"] -and -not [string]::IsNullOrWhiteSpace([string]$parsed.url)) {
            $url = ([string]$parsed.url).Trim().TrimEnd('/')
        }

        if ($null -ne $parsed.PSObject.Properties["settings"] -and
            $null -ne $parsed.settings -and
            $null -ne $parsed.settings.PSObject.Properties["PhotoIdentity__DatabasePath"] -and
            -not [string]::IsNullOrWhiteSpace([string]$parsed.settings.PhotoIdentity__DatabasePath)) {
            $resolvedDatabase = Resolve-ConfiguredPath -Value ([string]$parsed.settings.PhotoIdentity__DatabasePath) -BaseDirectory $configurationDirectory
        }
    }

    return [pscustomobject]@{
        ConfigurationPath = $configurationPath
        DatabasePath = [IO.Path]::GetFullPath($resolvedDatabase)
        Url = $url
    }
}

function New-RehearsalLauncherConfiguration {
    param(
        [string]$BaseConfigurationPath,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string]$PostgresEnvironmentName,
        [Parameter(Mandatory = $true)][string]$Url
    )

    if ([string]::IsNullOrWhiteSpace($BaseConfigurationPath)) {
        $configuration = [pscustomobject][ordered]@{
            url = $Url
            postgresConnectionEnvironmentVariable = $PostgresEnvironmentName
            settings = [pscustomobject][ordered]@{
                PhotoIdentity__CatalogueProvider = "postgresql"
            }
        }
    }
    else {
        $configuration = Get-Content -LiteralPath $BaseConfigurationPath -Raw | ConvertFrom-Json
        if ($null -eq $configuration.PSObject.Properties["postgresConnectionEnvironmentVariable"]) {
            Add-Member -InputObject $configuration -MemberType NoteProperty -Name "postgresConnectionEnvironmentVariable" -Value $PostgresEnvironmentName
        }
        else {
            $configuration.postgresConnectionEnvironmentVariable = $PostgresEnvironmentName
        }

        if ($null -eq $configuration.PSObject.Properties["settings"] -or $null -eq $configuration.settings) {
            Add-Member -InputObject $configuration -MemberType NoteProperty -Name "settings" -Value ([pscustomobject]@{}) -Force
        }
        if ($null -eq $configuration.settings.PSObject.Properties["PhotoIdentity__CatalogueProvider"]) {
            Add-Member -InputObject $configuration.settings -MemberType NoteProperty -Name "PhotoIdentity__CatalogueProvider" -Value "postgresql"
        }
        else {
            $configuration.settings.PhotoIdentity__CatalogueProvider = "postgresql"
        }
    }

    # Rehearsal UI review is local-only. Do not inherit operator mobile certificate
    # paths or password-environment requirements into the temporary launcher file.
    Add-Member -InputObject $configuration -MemberType NoteProperty -Name "mobileAccess" -Value ([pscustomobject][ordered]@{
        enabled = $false
    }) -Force

    $configuration | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
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

    & dotnet run --project $cliProject --configuration Release --no-build -- @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Photo Identity CLI exited with code $LASTEXITCODE."
    }
}

function Assert-TargetDatabaseName {
    param([Parameter(Mandatory = $true)][string]$Name)
    if ($Name -notmatch '^[a-z][a-z0-9_]{0,62}$') {
        throw "Target database names must start with a letter and contain only lowercase letters, digits and underscores (maximum 63 characters)."
    }
}

function New-FreshPostgresDatabase {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerId,
        [Parameter(Mandatory = $true)][string]$DatabaseName,
        [Parameter(Mandatory = $true)]$Podman
    )

    Write-Host "Creating fresh rehearsal database '$DatabaseName'..."
    & $Podman.Source exec -e "TARGET_DB=$DatabaseName" $ContainerId `
        sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" createdb -h 127.0.0.1 -U "$POSTGRES_USER" "$TARGET_DB"'
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the fresh rehearsal PostgreSQL database '$DatabaseName'."
    }
}

function Remove-PostgresDatabase {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerId,
        [Parameter(Mandatory = $true)][string]$DatabaseName,
        [Parameter(Mandatory = $true)]$Podman
    )

    & $Podman.Source exec -e "TARGET_DB=$DatabaseName" $ContainerId `
        sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" dropdb --if-exists --force -h 127.0.0.1 -U "$POSTGRES_USER" "$TARGET_DB"' *> $null
}

function Invoke-MigrationTarget {
    param(
        [Parameter(Mandatory = $true)][string]$BackupPath,
        [Parameter(Mandatory = $true)][string]$ReportPath,
        [Parameter(Mandatory = $true)][string]$ConnectionString,
        [Parameter(Mandatory = $true)][string]$EnvironmentName
    )

    $previous = [Environment]::GetEnvironmentVariable($EnvironmentName, "Process")
    try {
        [Environment]::SetEnvironmentVariable($EnvironmentName, $ConnectionString, "Process")
        Invoke-Cli -Arguments @(
            "catalogue", "migrate",
            "--sqlite-backup", $BackupPath,
            "--postgres-connection-env", $EnvironmentName,
            "--report", $ReportPath
        )
    }
    finally {
        [Environment]::SetEnvironmentVariable($EnvironmentName, $previous, "Process")
    }
}

function Get-StableMigrationReportJson {
    param([Parameter(Mandatory = $true)][string]$Path)

    $report = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $tables = @($report.Tables | Sort-Object Table | ForEach-Object {
        [ordered]@{
            Table = [string]$_.Table
            SqliteRows = [long]$_.SqliteRows
            PostgresRows = [long]$_.PostgresRows
        }
    })
    $critical = [ordered]@{}
    foreach ($property in @($report.CriticalCounts.PSObject.Properties | Sort-Object Name)) {
        $critical[$property.Name] = [long]$property.Value
    }

    $stable = [ordered]@{
        SchemaVersion = [int]$report.SchemaVersion
        SourceFileName = [string]$report.SourceFileName
        SourceSha256 = ([string]$report.SourceSha256).ToLowerInvariant()
        SourceBytes = [long]$report.SourceBytes
        SqliteSchemaVersion = [int]$report.SqliteSchemaVersion
        PostgresSchemaVersion = [int]$report.PostgresSchemaVersion
        RowsCopied = [long]$report.RowsCopied
        SequencesRepaired = [int]$report.SequencesRepaired
        Validation = [string]$report.Validation
        Tables = $tables
        CriticalCounts = $critical
    }
    return ($stable | ConvertTo-Json -Depth 8 -Compress)
}

function Assert-EquivalentMigrationReports {
    param(
        [Parameter(Mandatory = $true)][string]$FirstReportPath,
        [Parameter(Mandatory = $true)][string]$SecondReportPath
    )

    $first = Get-StableMigrationReportJson -Path $FirstReportPath
    $second = Get-StableMigrationReportJson -Path $SecondReportPath
    if ($first -cne $second) {
        throw "Repeatability validation failed: the two migration reports differ in stable source/schema/count/sequence evidence."
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
$primaryReportPath = Join-Path $BackupDirectory "postgres-migration-$timestamp-primary.json"
$secondaryReportPath = Join-Path $BackupDirectory "postgres-migration-$timestamp-repeat.json"
$rehearsalLauncherPath = Join-Path $BackupDirectory "launcher-rehearsal-$timestamp.json"
if ([string]::IsNullOrWhiteSpace($TargetDatabaseName)) {
    $TargetDatabaseName = "photoidentity_rehearsal_${timestamp}_a$([Guid]::NewGuid().ToString('N').Substring(0, 6))".ToLowerInvariant()
}
if ([string]::IsNullOrWhiteSpace($SecondaryTargetDatabaseName)) {
    $SecondaryTargetDatabaseName = "photoidentity_rehearsal_${timestamp}_b$([Guid]::NewGuid().ToString('N').Substring(0, 6))".ToLowerInvariant()
}
Assert-TargetDatabaseName -Name $TargetDatabaseName
Assert-TargetDatabaseName -Name $SecondaryTargetDatabaseName
if ($TargetDatabaseName -eq $SecondaryTargetDatabaseName) {
    throw "Primary and secondary rehearsal PostgreSQL database names must differ."
}

Write-Host "Building the Release CLI used for backup and migration rehearsal..."
& dotnet build $cliProject --configuration Release | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "CLI Release build failed with code $LASTEXITCODE."
}

Write-Host "Creating one consistent stopped-source SQLite backup for both imports..."
Invoke-Cli -Arguments @(
    "catalogue", "backup",
    "--database", $sourcePath,
    "--output", $backupPath,
    "--application-stopped"
)
$backupHashBeforeMigration = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant()
(Get-Item -LiteralPath $backupPath).IsReadOnly = $true

$settings = Read-DotEnv -Path $EnvironmentPath
foreach ($required in @("PHOTOIDENTITY_POSTGRES_DATABASE", "PHOTOIDENTITY_POSTGRES_USER", "PHOTOIDENTITY_POSTGRES_PASSWORD", "PHOTOIDENTITY_POSTGRES_PORT")) {
    if (-not $settings.ContainsKey($required) -or [string]::IsNullOrWhiteSpace([string]$settings[$required])) {
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
$createdTargets = @()
$successfulTargets = @()
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

        New-FreshPostgresDatabase -ContainerId $containerId -DatabaseName $TargetDatabaseName -Podman $podman
        $createdTargets += $TargetDatabaseName
        New-FreshPostgresDatabase -ContainerId $containerId -DatabaseName $SecondaryTargetDatabaseName -Podman $podman
        $createdTargets += $SecondaryTargetDatabaseName
    }
    finally {
        Pop-Location
    }

    $primaryConnection = New-TargetConnectionString -Settings $settings -DatabaseName $TargetDatabaseName
    $secondaryConnection = New-TargetConnectionString -Settings $settings -DatabaseName $SecondaryTargetDatabaseName

    Write-Host "Migrating the preserved backup into primary rehearsal target..."
    Invoke-MigrationTarget -BackupPath $backupPath -ReportPath $primaryReportPath -ConnectionString $primaryConnection -EnvironmentName "PHOTOIDENTITY_REHEARSAL_PRIMARY_CONNECTION_STRING"
    $successfulTargets += $TargetDatabaseName

    Write-Host "Migrating the exact same preserved backup into repeatability target..."
    Invoke-MigrationTarget -BackupPath $backupPath -ReportPath $secondaryReportPath -ConnectionString $secondaryConnection -EnvironmentName "PHOTOIDENTITY_REHEARSAL_SECONDARY_CONNECTION_STRING"
    $successfulTargets += $SecondaryTargetDatabaseName

    Assert-EquivalentMigrationReports -FirstReportPath $primaryReportPath -SecondReportPath $secondaryReportPath
    $backupHashAfterMigration = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($backupHashAfterMigration -ne $backupHashBeforeMigration) {
        throw "The preserved SQLite backup changed during migration rehearsal."
    }

    Write-Host ""
    Write-Host "Real-catalogue repeatable migration rehearsal passed."
    Write-Host "primary-rehearsal-database: $TargetDatabaseName"
    Write-Host "secondary-rehearsal-database: $SecondaryTargetDatabaseName"
    Write-Host "backup: $backupPath"
    Write-Host "backup-sha256: $backupHashAfterMigration"
    Write-Host "primary-report: $primaryReportPath"
    Write-Host "secondary-report: $secondaryReportPath"
    Write-Host "repeatability: passed"
    Write-Host "production-authority-changed: false"

    if ($LaunchForReview) {
        if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
            throw "Launcher was not found: $launcherPath"
        }

        $runtimeEnvironmentName = "PHOTOIDENTITY_REHEARSAL_RUNTIME_CONNECTION_STRING"
        $previousRuntimeConnection = [Environment]::GetEnvironmentVariable($runtimeEnvironmentName, "Process")
        try {
            [Environment]::SetEnvironmentVariable($runtimeEnvironmentName, $primaryConnection, "Process")
            New-RehearsalLauncherConfiguration -BaseConfigurationPath $launcherInfo.ConfigurationPath -OutputPath $rehearsalLauncherPath -PostgresEnvironmentName $runtimeEnvironmentName -Url $launcherInfo.Url

            Write-Host "Starting Photo Identity against the primary rehearsal PostgreSQL database for UI acceptance..."
            & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $launcherPath -ConfigurationPath $rehearsalLauncherPath
            if ($LASTEXITCODE -ne 0) {
                throw "Photo Identity launcher failed with code $LASTEXITCODE."
            }

            $health = Invoke-RestMethod -Method Get -Uri "$($launcherInfo.Url)/health" -TimeoutSec 5
            if ([string]$health.status -ne "ok" -or [string]$health.catalogueProvider -ne "postgresql") {
                throw "Rehearsal runtime health did not confirm catalogueProvider=postgresql."
            }
            Write-Host "rehearsal-launcher-config: $rehearsalLauncherPath"
            Write-Host "rehearsal-runtime-health: postgresql"
        }
        finally {
            [Environment]::SetEnvironmentVariable($runtimeEnvironmentName, $previousRuntimeConnection, "Process")
        }
    }
}
finally {
    foreach ($name in $settings.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousComposeEnvironment[$name], "Process")
    }

    if (-not [string]::IsNullOrWhiteSpace($containerId)) {
        foreach ($target in $createdTargets) {
            if ($successfulTargets -notcontains $target) {
                Write-Warning "Removing incomplete rehearsal PostgreSQL target '$target'."
                Remove-PostgresDatabase -ContainerId $containerId -DatabaseName $target -Podman $podman
            }
        }
    }
}
