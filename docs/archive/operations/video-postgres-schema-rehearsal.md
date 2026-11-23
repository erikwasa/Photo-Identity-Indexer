# WI-0151 — PostgreSQL v32 to v33 restored-database rehearsal

> **Historical WI-0151 rehearsal note (2026-10-19):** This procedure
> documented the original v32 → v33 acceptance. From WI-0188 checkpoint 2,
> the current application targets schema **v34**; the legacy
> `catalogue rehearse-v33` command on a verified v32 restored database now
> applies both v33 and v34. For a maintained **v33 → v34** upgrade, use the
> separate [v34 queue rehearsal](video-processing-job-queue-v34.md), not
> this historical procedure. The prior v33 acceptance evidence remains valid.

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
.\verify-postgres-backup-restore.ps1 -BackupPath $backupPath -ApplicationStopped -RetainRestoredDatabase

$restoreReportPath = "$backupPath.restore-verification.json"
$restoreReport = Get-Content $restoreReportPath -Raw | ConvertFrom-Json
if (-not $restoreReport.passed) { throw 'Restore verification failed.' }
$rehearsalDb = [string]$restoreReport.verificationDatabase
$rehearsalDb
~~~

The restore verifier now removes successful verification databases by default. This rehearsal passes -RetainRestoredDatabase explicitly because the schema rehearsal needs the actual isolated database. After the rehearsal has been reviewed, retire that exact owned copy using manage-postgres-restores.ps1 as described in the PostgreSQL operations runbook. The report contains the source and restored database names, original schema and per-table counts.

## Select the local PostgreSQL credentials without rewriting the database

**The restore verification report now selects the target database.** The CLI uses your private launcher connection string only to get localhost server settings and credentials. It overrides any embedded database name with the exact verification database from the report, and verifies its name, source mismatch, v32 schema and table counts before running a migration. This avoids the PowerShell connection-string alias/PSObject issues encountered during maintainer verification.

Do not copy your production connection string into GitHub, a log, or a conversation. The following reads it from the configured launcher environment variable into a **temporary process-scoped variable** without printing it.

~~~powershell
$ErrorActionPreference = 'Stop'
Remove-Item Env:\PHOTOIDENTITY_WI0151_REHEARSAL -ErrorAction SilentlyContinue

$restoreReportPath = "$backupPath.restore-verification.json"
$restoreReport = Get-Content $restoreReportPath -Raw | ConvertFrom-Json
if (-not $restoreReport.passed) { throw 'Restore verification failed.' }
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

# Pass credentials unchanged; the CLI selects the target only from the verified report.
$env:PHOTOIDENTITY_WI0151_REHEARSAL = [string]$connectionString
Write-Host "Verified rehearsal target: $($restoreReport.verificationDatabase)"
~~~

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
