# WI-0151 — PostgreSQL v32 to v33 restored-database rehearsal

This is an explicit, operator-controlled migration rehearsal, not production rollout. It is required because fresh-database unit tests do not prove schema migration on an actual maintained catalogue.

## Preconditions

- Maintainer verified the Docker Compose/Podman setup and all 56 PostgreSQL persistence tests plus seven integration/composition tests on 2026-10-15.
- Stop Photo Identity, including its API/background workers, before taking a count-stable PostgreSQL backup. Leave Podman PostgreSQL running.
- Confirm which database is the maintained production catalogue. Do not infer it from dated rehearsal database names.
- Do not download or reprocess videos for this migration.

## Back up and restore

In PowerShell from the repository root:

~~~powershell
$productionDb = 'REPLACE_WITH_CONFIRMED_PRODUCTION_DB'
$backupPath = Join-Path $env:LOCALAPPDATA ('PhotoIdentity\backups\postgresql\pre-video-v33-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.dump')

.\backup-postgres-catalogue.ps1 -DatabaseName $productionDb -OutputPath $backupPath
.\verify-postgres-backup-restore.ps1 -BackupPath $backupPath -ApplicationStopped

$restoreReportPath = "$backupPath.restore-verification.json"
$restoreReport = Get-Content $restoreReportPath -Raw | ConvertFrom-Json
if (-not $restoreReport.passed) { throw 'Restore verification failed.' }
$rehearsalDb = [string]$restoreReport.verificationDatabase
$rehearsalDb
~~~

This restore verifier creates a unique database named photoidentity_restore_* and retains it for inspection. The report contains the source and restored database names, original schema and per-table counts.

## Point a process-only connection variable to the restored copy

The rehearsal CLI never accepts a raw connection string argument. The following reads the configured launcher credentials directly from Windows environment-variable scopes and changes **only** the database name in a process-local connection string. It never prints the password.

**Why this is different:** PowerShell can wrap a pipeline result in a PSObject; assigning it directly to the .NET DbConnectionStringBuilder.ConnectionString setter can fail. Use a direct, explicitly typed string from GetEnvironmentVariable instead of the previous ForEach-Object / Select-Object pipeline. Stop on error and do not proceed to migration if parsing fails.

~~~powershell
$ErrorActionPreference = 'Stop'
Remove-Item Env:\PHOTOIDENTITY_WI0151_REHEARSAL -ErrorAction SilentlyContinue

$restoreReport = Get-Content $restoreReportPath -Raw | ConvertFrom-Json
if (-not $restoreReport.passed) { throw 'Restore verification failed.' }
$rehearsalDb = [string]$restoreReport.verificationDatabase
if ([int]$restoreReport.restoredEvidence.schemaVersion -ne 32) {
    throw "Expected restored schema v32; aborting rehearsal."
}

$launcherPath = @(
    $env:PHOTOIDENTITY_LAUNCHER_CONFIG
    (Join-Path (Get-Location) 'PhotoIdentity.launcher.json')
    (Join-Path $env:LOCALAPPDATA 'PhotoIdentity\launcher.json')
) | Where-Object {
    $_ -and (Test-Path -LiteralPath $_ -PathType Leaf)
} | Select-Object -First 1
if (-not $launcherPath) { throw 'Launcher configuration not found.' }

$launcher = Get-Content $launcherPath -Raw | ConvertFrom-Json
$name = [string]$launcher.postgresConnectionEnvironmentVariable
if (-not $name) { throw 'Missing launcher PostgreSQL environment-variable name.' }

[string]$connectionString = ''
foreach ($scope in @(
    [EnvironmentVariableTarget]::Process,
    [EnvironmentVariableTarget]::User,
    [EnvironmentVariableTarget]::Machine
)) {
    [string]$candidate = [Environment]::GetEnvironmentVariable($name, $scope)
    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
        $connectionString = $candidate
        break
    }
}
if (-not $connectionString) {
    throw 'Launcher-selected PostgreSQL connection variable is unavailable.'
}

$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.ConnectionString = [string]$connectionString
if ($builder.ContainsKey('Initial Catalog')) {
    $null = $builder.Remove('Initial Catalog')
}
$builder['Database'] = $rehearsalDb
$env:PHOTOIDENTITY_WI0151_REHEARSAL = [string]$builder.ConnectionString
Write-Host "Rehearsal target: $rehearsalDb"
~~~

If your launcher is configured elsewhere, use its actual private path. Do not paste connection strings or secrets into GitHub. A previous Cannot convert object of type System.Management.Automation.PSObject error means the earlier snippet failed in PowerShell, not in the database migration. Discard the incomplete rehearsal environment variable and repeat the corrected setup.

## Rehearse the migration

~~~powershell
try {
    dotnet run --project src/PhotoIdentity.Cli --configuration Release -- catalogue rehearse-v33 --postgres-connection-env PHOTOIDENTITY_WI0151_REHEARSAL --restore-report $restoreReportPath
    if ($LASTEXITCODE -ne 0) { throw "Schema rehearsal failed with exit code $LASTEXITCODE." }
}
finally {
    Remove-Item Env:\PHOTOIDENTITY_WI0151_REHEARSAL -ErrorAction SilentlyContinue
}
~~~

The command checks, **before modifying anything**, that the selected database is local, its name matches the successful restore verification report, its name begins with photoidentity_restore_, it differs from the production source database, its schema is v32, and its original per-table counts equal the report.

It then migrates **only that database** to v33, runs initialization again for idempotency, and checks all original table row counts except the expected new migration-history entry. All existing asset and revision rows must remain photo; the new video metadata and poster tables must be empty. It reports success without printing the connection string.

A failed rehearsal must be investigated before updating your maintained app. Leave the restored database intact for diagnosis. The existing dump is private and must remain protected.

## After the restored-catalogue rehearsal passes

Only after the rehearsal succeeds and you have a verified backup, use the normal packaging/startup workflow. Require /health to report PostgreSQL ready and schemaVersion 33. Verify representative photo counts, galleries, people, Smart and Creative Collections, slideshows and normal archive behavior. Video assets are not visible yet: this is only a schema foundation.

Next WI-0151 development work remains opt-in source/revision persistence, exclusion-aware poster lifecycle, photo-only query guards, library browsing and HDR/HLG still-poster tone mapping. Keep WI-0151 in progress.
