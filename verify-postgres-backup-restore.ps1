[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$VerificationDatabaseName,
    [switch]$ApplicationStopped
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $ApplicationStopped) {
    throw "Stop Photo Identity and rerun with -ApplicationStopped so source and restored row counts are deterministic."
}

$BackupPath = [IO.Path]::GetFullPath($BackupPath)
if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) {
    throw "Backup file was not found: $BackupPath"
}

$backupReportPath = "$BackupPath.json"
if (-not (Test-Path -LiteralPath $backupReportPath -PathType Leaf)) {
    throw "Backup report was not found: $backupReportPath"
}

$backupReport = Get-Content -LiteralPath $backupReportPath -Raw | ConvertFrom-Json
$sourceDatabase = [string]$backupReport.sourceDatabase
if ([string]::IsNullOrWhiteSpace($sourceDatabase)) {
    throw "The backup report does not identify the source database."
}
if ([string]$backupReport.format -ne "pg_dump-custom") {
    throw "The backup report does not describe the expected pg_dump custom format."
}

$actualHash = (Get-FileHash -LiteralPath $BackupPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne ([string]$backupReport.backupSha256).ToLowerInvariant()) {
    throw "Backup SHA-256 no longer matches its report. Do not restore modified backup bytes."
}

$runningIds = @()
$runningIds += @(Get-Process -Name "PhotoIdentity.Api" -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
try {
    $runningIds += @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.Name -ieq "dotnet.exe" -and
        -not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
        $_.CommandLine -like "*PhotoIdentity.Api*"
    } | ForEach-Object { [int]$_.ProcessId })
}
catch {
    Write-Verbose "Could not inspect dotnet command lines for the additional application quiescence check."
}
$runningIds = @($runningIds | Sort-Object -Unique)
if ($runningIds.Count -gt 0) {
    throw "Photo Identity still appears to be running (process id(s): $($runningIds -join ', '))."
}

$podman = Get-Command podman -ErrorAction SilentlyContinue
if ($null -eq $podman) {
    throw "Podman is required for PostgreSQL restore verification."
}

$composeDirectory = Join-Path $PSScriptRoot "deploy\postgres"
$containerBackupPath = $null
$containerId = $null

function Invoke-DatabaseQuery {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerId,
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $rows = @(
        & $podman.Source exec `
            -e "TARGET_DATABASE=$Database" `
            -e "PHOTOIDENTITY_SQL=$Sql" `
            $ContainerId sh -lc `
            'psql -X -A -t -U "$POSTGRES_USER" -d "$TARGET_DATABASE" -v ON_ERROR_STOP=1 -c "$PHOTOIDENTITY_SQL"' `
            2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL verification query failed."
    }

    @($rows | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -ne "" })
}

function Get-CatalogueEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerId,
        [Parameter(Mandatory = $true)][string]$Database
    )

    $schemaRows = @(Invoke-DatabaseQuery -ContainerId $ContainerId -Database $Database -Sql "SELECT COALESCE(MAX(version), 0) FROM photo_identity_schema_migrations;")
    $tableRows = @(Invoke-DatabaseQuery -ContainerId $ContainerId -Database $Database -Sql "SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname = 'public' ORDER BY tablename;")
    $counts = [ordered]@{}

    foreach ($table in $tableRows) {
        if ($table -notmatch '^[a-z0-9_]+$') {
            throw "Unexpected public table name '$table'."
        }
        $countRows = @(Invoke-DatabaseQuery -ContainerId $ContainerId -Database $Database -Sql "SELECT COUNT(*) FROM public.$table;")
        if ($countRows.Count -ne 1) {
            throw "Could not count table '$table'."
        }
        $counts[$table] = [long]$countRows[0]
    }

    $constraintRows = @(Invoke-DatabaseQuery -ContainerId $ContainerId -Database $Database -Sql "SELECT COUNT(*) FROM pg_catalog.pg_constraint WHERE connamespace = 'public'::regnamespace AND NOT convalidated;")

    [pscustomobject][ordered]@{
        schemaVersion = [int]$schemaRows[0]
        publicTableCount = $tableRows.Count
        unvalidatedConstraintCount = [long]$constraintRows[0]
        tableCounts = $counts
    }
}

Push-Location $composeDirectory
try {
    $containerId = (& $podman.Source compose ps -q postgres).Trim()
    if ([string]::IsNullOrWhiteSpace($containerId)) {
        throw "The repository PostgreSQL service is not running. Run ./verify-postgres.ps1 first."
    }

    if ([string]::IsNullOrWhiteSpace($VerificationDatabaseName)) {
        $VerificationDatabaseName = "photoidentity_restore_$((Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss'))_$([Guid]::NewGuid().ToString('N').Substring(0, 6))"
    }
    if ($VerificationDatabaseName -notmatch '^[a-z][a-z0-9_]{0,62}$') {
        throw "Verification database name must contain only lowercase letters, digits and underscores and fit PostgreSQL's 63-character identifier limit."
    }
    if ($VerificationDatabaseName -eq $sourceDatabase) {
        throw "The verification database must not be the production source database."
    }

    $sourceEvidence = Get-CatalogueEvidence -ContainerId $containerId -Database $sourceDatabase

    $exists = @(
        & $podman.Source exec `
            -e "VERIFY_DATABASE=$VerificationDatabaseName" `
            $containerId sh -lc `
            'psql -X -A -t -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -c "SELECT 1 FROM pg_database WHERE datname = ''$VERIFY_DATABASE''"' `
            2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not check the verification database name."
    }
    if ((($exists -join " ").Trim()) -eq "1") {
        throw "Verification database '$VerificationDatabaseName' already exists; choose another name."
    }

    Write-Host "Creating isolated restore-verification database '$VerificationDatabaseName'..."
    & $podman.Source exec `
        -e "VERIFY_DATABASE=$VerificationDatabaseName" `
        $containerId sh -lc `
        'createdb -U "$POSTGRES_USER" --template=template0 "$VERIFY_DATABASE"' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the isolated restore-verification database."
    }

    $containerBackupPath = "/tmp/photoidentity-restore-$([Guid]::NewGuid().ToString('N')).dump"
    $containerDestination = "$containerId`:$containerBackupPath"
    & $podman.Source cp $BackupPath $containerDestination | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not copy the PostgreSQL backup into the container for isolated restore verification."
    }

    Write-Host "Restoring the backup into the isolated database..."
    & $podman.Source exec `
        -e "VERIFY_DATABASE=$VerificationDatabaseName" `
        -e "DUMP_PATH=$containerBackupPath" `
        $containerId sh -lc `
        'pg_restore -U "$POSTGRES_USER" -d "$VERIFY_DATABASE" --exit-on-error --single-transaction --no-owner --no-acl "$DUMP_PATH"' `
        2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed. The production database was not modified. The isolated verification database was retained for diagnosis."
    }

    $restoredEvidence = Get-CatalogueEvidence -ContainerId $containerId -Database $VerificationDatabaseName
    if ($sourceEvidence.schemaVersion -ne $restoredEvidence.schemaVersion) {
        throw "Restored schema version does not match the stopped production source."
    }
    if ($restoredEvidence.unvalidatedConstraintCount -ne 0) {
        throw "The restored database contains unvalidated public constraints."
    }

    $sourceTables = @($sourceEvidence.tableCounts.Keys)
    $restoredTables = @($restoredEvidence.tableCounts.Keys)
    if (($sourceTables -join "`n") -ne ($restoredTables -join "`n")) {
        throw "The restored public-table set does not match the stopped production source."
    }
    foreach ($table in $sourceTables) {
        if ([long]$sourceEvidence.tableCounts[$table] -ne [long]$restoredEvidence.tableCounts[$table]) {
            throw "Restored row count for '$table' does not match the stopped production source."
        }
    }

    $report = [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        backupPath = $BackupPath
        backupSha256 = $actualHash
        sourceDatabase = $sourceDatabase
        verificationDatabase = $VerificationDatabaseName
        passed = $true
        sourceEvidence = $sourceEvidence
        restoredEvidence = $restoredEvidence
        cleanupRequired = $true
        privacyNote = "Credentials and connection strings are omitted. The isolated verification database is intentionally retained until the maintainer finishes inspection."
    }
    $reportPath = "$BackupPath.restore-verification.json"
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding UTF8

    Write-Host "PostgreSQL isolated restore verification passed."
    Write-Host "source database: $sourceDatabase"
    Write-Host "verification database: $VerificationDatabaseName"
    Write-Host "report: $reportPath"
    Write-Host "The verification database was retained intentionally. Follow the operations runbook to remove it after review."
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($containerBackupPath) -and -not [string]::IsNullOrWhiteSpace($containerId)) {
        & $podman.Source exec -e "DUMP_PATH=$containerBackupPath" $containerId sh -lc 'rm -f "$DUMP_PATH"' *> $null
    }
    Pop-Location
}
