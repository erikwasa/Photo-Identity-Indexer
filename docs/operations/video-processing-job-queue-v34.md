# WI-0188 — isolated PostgreSQL video queue v34

> Historical checkpoint 2 rollout procedure for schema v34. The latest
> implementation now targets **schema v35**. If your maintained catalogue is
> already v34, follow [the v35 backup/rehearsal gate](video-processing-control-v35.md)
> instead; `catalogue rehearse-v34` is only for a verified isolated v33
> restore and now applies all later migrations too.

**Development checkpoint:** v34 adds only the control table video_processing_jobs. It does not enable API endpoints, background video workers, OneDrive hydration, FFmpeg, proxy generation, or automatic enqueue. The accepted v33 photo/video data remains unchanged.

## Safety and backup before app startup

**Do not start a v34 packaged build against the maintained v33 database without a verified backup and an isolated restored-catalogue rehearsal.** The normal app startup initializes and upgrades its connected catalogue. Identify the *actual app-connected database* through the launcher and live connection, not from an old rehearsal name. Stop Photo Identity before taking the backup, but leave PostgreSQL running.

From the repository root in PowerShell:

~~~powershell
$ErrorActionPreference = 'Stop'
$databaseName = 'REPLACE_WITH_CONFIRMED_APP_CONNECTED_DB'
$backupPath = Join-Path $env:LOCALAPPDATA (
  'PhotoIdentity\backups\postgresql\pre-wi0188-v34-' +
  (Get-Date -Format 'yyyyMMdd-HHmmss') + '.dump')
.\backup-postgres-catalogue.ps1 -DatabaseName $databaseName -OutputPath $backupPath
.\verify-postgres-backup-restore.ps1 -BackupPath $backupPath -ApplicationStopped -RetainRestoredDatabase

$restoreReportPath = "$backupPath.restore-verification.json"
$report = Get-Content -LiteralPath $restoreReportPath -Raw | ConvertFrom-Json
if (-not $report.passed) { throw 'Backup/isolated restore verification failed.' }
if ([int]$report.restoredEvidence.schemaVersion -ne 33) {
    throw 'Expected isolated restored schema v33. No upgrade authorized.'
}
~~~

The report identifies the distinct local photoidentity_restore_* database. Keep it private. Do not post the connection string, report contents, source paths or real database names.

## Rehearse v33 → v34 on the verified isolated restore ONLY

The CLI command uses the validated restore report to override any database name in the credentials. It accepts only localhost, checks the original schema, compares source table counts with the restore report before migration, runs schema initialization twice on the restored database, and verifies unchanged original table counts (except migration history +1), exactly one v34 migration, and an empty new queue.

~~~powershell
$launcherPath = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\launcher.json'
$launcher = Get-Content -LiteralPath $launcherPath -Raw | ConvertFrom-Json
$name = [string]$launcher.postgresConnectionEnvironmentVariable
if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw 'Invalid launcher DB variable.' }
$connectionString = @(
    [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::GetEnvironmentVariable($name, 'User')
    [Environment]::GetEnvironmentVariable($name, 'Machine')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
if (-not $connectionString) { throw 'Launcher credentials unavailable.' }
$env:PHOTOIDENTITY_WI0188_REHEARSAL = $connectionString
try {
    dotnet run --project src/PhotoIdentity.Cli -c Release -- catalogue rehearse-v34 --postgres-connection-env PHOTOIDENTITY_WI0188_REHEARSAL --restore-report $restoreReportPath
    if ($LASTEXITCODE -ne 0) { throw 'Schema v34 rehearsal failed.' }
}
finally {
    Remove-Item Env:\PHOTOIDENTITY_WI0188_REHEARSAL -ErrorAction SilentlyContinue
    Remove-Variable connectionString -ErrorAction SilentlyContinue
}
~~~

Require the output to report **Schema v33 -> v34 rehearsal passed**, unchanged original table counts, zero queue rows and idempotent initialization. If it fails, do not upgrade the maintained app; retain the isolated database for investigation.

The older 'catalogue rehearse-v33' name is retained for a verified **v32** restore. In current code it necessarily applies both v33 and v34. Never run it against the maintained v33 database.

## Post-rehearsal rollout

Once the verified backup and isolated rehearsal pass, package/restart normally and check that /health reports schemaVersion 34 and PostgreSQL ready. Revisit Archive, photo searches, Smart and Creative Collections, slideshows, and optional read-only Videos library. New video job processing remains disabled; no video queue UI or heavy worker is registered.

**Share only aggregate pass/fail** for backup, isolated restore, migration, source-table counts, empty queue and default-off smoke test. Never share personal media or database identifiers.

### Limits of checkpoint 2

The durable job table stores revision IDs, deterministic hashes, bounded estimates, lease, retry/cancel and sanitized status only. Existing photo processing_jobs and processing_runs are untouched. A lease is NOT authorization to read source bytes or publish artifacts: future execution must independently verify local availability, current revision and exclusions, enforce CPU/GPU/IO/memory/time/temp quotas and atomically publish or clean up derivatives. The maintained Windows mixed-load benchmark remains an WI-0188 acceptance gate.


> WI-0208: this rehearsal deliberately uses `-RetainRestoredDatabase` because the next CLI step needs the existing restored database. Successful standalone verification normally deletes its owned restored copy. After the rehearsal, use the exact-name, ownership-validated `manage-postgres-restores.ps1` procedure in [PostgreSQL operations](postgresql-operations.md); do not delete production or backup dumps.
