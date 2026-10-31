[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$VerificationDatabaseName,
    [switch]$ApplicationStopped,
    [switch]$ResumeRestoredDatabase,
    [switch]$RetainRestoredDatabase
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
if ($ResumeRestoredDatabase -and (
        [string]::IsNullOrWhiteSpace($VerificationDatabaseName) -or
        $VerificationDatabaseName -notmatch '^photoidentity_restore_[0-9]{14}_[a-f0-9]{6}$')) {
    throw "ResumeRestoredDatabase requires the exact generated photoidentity_restore_YYYYMMDDHHMMSS_xxxxxx database name."
}
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
$restoreCreated = $false
$verificationPassed = $false
$ownershipMarker = $null
$reportPath = "$BackupPath.restore-verification.json"
. (Join-Path $PSScriptRoot 'postgres-restore-lifecycle.ps1')

function Invoke-DatabaseQuery {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerId,
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    # Keep PostgreSQL errors so a missing restored relation, permission failure,
    # or transient container problem is diagnosable without database secrets.
    $stderrPath = Join-Path ([IO.Path]::GetTempPath()) (
        "photoidentity-restore-query-$([Guid]::NewGuid().ToString('N')).stderr")
    try {
        $rows = @(
            & $podman.Source exec `
                -e "TARGET_DATABASE=$Database" `
                -e "PHOTOIDENTITY_SQL=$Sql" `
                $ContainerId sh -lc `
                'psql -X -A -t -U "$POSTGRES_USER" -d "$TARGET_DATABASE" -v ON_ERROR_STOP=1 -c "$PHOTOIDENTITY_SQL"' `
                2> $stderrPath)
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            $detail = ""
            if (Test-Path -LiteralPath $stderrPath -PathType Leaf) {
                $detail = (@(Get-Content -LiteralPath $stderrPath -ErrorAction SilentlyContinue |
                    Select-Object -Last 8) -join " ").Trim()
            }
            if ($detail.Length -gt 1200) { $detail = $detail.Substring(0, 1200) }
            throw "PostgreSQL verification query failed in database '$Database' (exit $exitCode; SQL: $Sql). $detail"
        }
        @($rows | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -ne "" })
    }
    finally {
        Remove-Item -LiteralPath $stderrPath -ErrorAction SilentlyContinue
    }
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
    if (-not (Test-GeneratedRestoreName $VerificationDatabaseName)) {
        throw 'Verification database must use the exact generated photoidentity_restore_YYYYMMDDHHMMSS_xxxxxx naming convention.'
    }
    if ($VerificationDatabaseName -eq $sourceDatabase) {
        throw "The verification database must not be the production source database."
    }

    $sourceEvidence = Get-CatalogueEvidence -ContainerId $containerId -Database $sourceDatabase
    $sourceSize = @(Invoke-DatabaseQuery -ContainerId $containerId -Database $sourceDatabase -Sql 'SELECT pg_database_size(current_database());')
    if ($sourceSize.Count -ne 1) { throw 'Cannot determine approximate PostgreSQL restore space requirement.' }
    $sourceDatabaseBytes = [long]$sourceSize[0]
    Write-Host ("Approximate source database size: {0:N2} GiB; a full isolated restore may need similar additional PostgreSQL volume space." -f ($sourceDatabaseBytes / 1GB))

    if ($ResumeRestoredDatabase) {
        Write-Host "Resuming read-only verification of retained restored database '$VerificationDatabaseName'..."
        # Resume is deliberately non-destructive even after a passing comparison.
        $existing = Get-RestoreDatabaseDetails -Name $VerificationDatabaseName
        if ($null -eq $existing) {
            throw "Retained verification database '$VerificationDatabaseName' does not exist."
        }
        $ownershipMarker = if ($existing.Owned) { $existing.Marker } else { $null }
    }
    else {
        $previousOwned = @(Find-ExistingOwnedRestoresForHash -BackupSha256 $actualHash)
        if ($previousOwned.Count -gt 0) {
            throw "Owned restored database(s) for this exact backup already exist: $($previousOwned -join ', '). Resume or explicitly retire the existing copy before creating a second one."
        }
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

        $restoreCreated = $true
        $ownershipMarker = "photoidentity-restore:v1:$([Guid]::NewGuid().ToString('N')):$actualHash"
        # Mark before copy/restore: interrupted or failed runs remain identifiable.
        Set-RestoreDatabaseOwnership -Name $VerificationDatabaseName -Marker $ownershipMarker
        Write-Host "Restore ownership recorded for '$VerificationDatabaseName'."
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

    $restoredDetails = Get-RestoreDatabaseDetails -Name $VerificationDatabaseName
    if ($null -eq $restoredDetails) { throw 'The verified restore disappeared before report publication.' }
    $verificationPassed = $true
    $retained = $ResumeRestoredDatabase -or $RetainRestoredDatabase
    $report = [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        backupPath = $BackupPath
        backupSha256 = $actualHash
        sourceDatabase = $sourceDatabase
        verificationDatabase = $VerificationDatabaseName
        passed = $true
        sourceEvidence = $sourceEvidence
        restoredEvidence = $restoredEvidence
        restoreOwnershipMarker = $ownershipMarker
        sourceDatabaseBytes = $sourceDatabaseBytes
        restoredDatabaseBytes = $restoredDetails.Bytes
        cleanupStatus = $(if ($retained) { 'retained' } else { 'pending' })
        cleanupRequired = [bool]$retained
        privacyNote = 'The backup and its SHA-256/report remain intact. Only owned, isolated restore databases may be cleaned up.'
    }
    # A durable passed report must exist before any destructive cleanup.
    Write-RestoreVerificationReport -Path $reportPath -Report $report

    if (-not $retained) {
        try {
            Remove-OwnedRestoreDatabase -Name $VerificationDatabaseName -ExpectedMarker $ownershipMarker -SourceDatabase $sourceDatabase
        }
        catch {
            $cleanupError = $_.Exception.Message
            $report.cleanupStatus = 'failed'
            $report.cleanupRequired = $true
            $report['cleanupError'] = $cleanupError
            try {
                Write-RestoreVerificationReport -Path $reportPath -Report $report
            }
            catch {
                Write-Warning 'Cleanup failed and the failure report could not be saved; inspect the database manually.'
            }
            throw "Verification passed but cleanup failed for '$VerificationDatabaseName': $cleanupError"
        }
        $report.cleanupStatus = 'removed'
        $report.cleanupRequired = $false
        try {
            Write-RestoreVerificationReport -Path $reportPath -Report $report
        }
        catch {
            throw "Owned restore '$VerificationDatabaseName' was removed, but the final cleanup report could not be written. The previously published report has cleanupStatus=pending."
        }
    }

    Write-Host 'PostgreSQL isolated restore verification passed.'
    Write-Host "source database: $sourceDatabase"
    Write-Host "verification database: $VerificationDatabaseName"
    Write-Host "report: $reportPath"
    Write-Host "cleanup: $($report.cleanupStatus)"
}
catch {
    if ($restoreCreated -and -not $verificationPassed) {
        $failureReport = [ordered]@{
            capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            backupSha256 = $actualHash
            sourceDatabase = $sourceDatabase
            verificationDatabase = $VerificationDatabaseName
            restoreOwnershipMarker = $ownershipMarker
            passed = $false
            cleanupStatus = 'retained-failure'
            cleanupRequired = $true
            privacyNote = 'Partial/failed restore preserved for diagnosis. Do not delete without exact ownership validation.'
        }
        try {
            Write-RestoreVerificationReport -Path $reportPath -Report $failureReport
        }
        catch {
            Write-Warning 'Failure report could not be published; the isolated database was retained.'
        }
        Write-Warning "Retained failed/partial verification database '$VerificationDatabaseName'. Use -ResumeRestoredDatabase or the operator inventory tool."
    }
    throw
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($containerBackupPath) -and -not [string]::IsNullOrWhiteSpace($containerId)) {
        & $podman.Source exec -e "DUMP_PATH=$containerBackupPath" $containerId sh -lc 'rm -f "$DUMP_PATH"' *> $null
    }
    Pop-Location
}
