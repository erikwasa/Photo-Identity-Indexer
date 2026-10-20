# WI-0188 checkpoint 6 — safe schema v35 video-lane control

> **Historical checkpoint only.** The current development build is v36.
> For the maintained database still at v34, perform ONE fresh backup and
> isolated **v34→v36** rehearsal using
> [the consolidated publication runbook](video-publication-and-v36-rehearsal.md).
> Do not use the old v35-only expected health/migration result below for
> the current branch.

**Do not start a v35 app on the maintained v34 catalogue until a CURRENT backup
and isolated v34 → v35 rehearsal succeed.** Maintainer previously accepted
v33 → v34 on 2026-10-19; this is a NEW migration and needs a new rehearsal.
You may defer that local work until other WI-0188 development checkpoints
are ready, but do not launch or package-deploy updated v35 code against the
maintained database in the meantime.

Checkpoint 6 adds the `video_processing_control` singleton and a
`control_generation` fence on existing video job records. The control is
**disabled and paused by default**. No worker is registered and none can be
enabled by the new CLI. There is no automatic enqueue, FFmpeg, hydration or
derivative publication.

## Operator rehearsal (when ready for consolidated local testing)

1. Update local `main`. Determine the **actual app-connected database** from
   the launcher and live PostgreSQL connection. Never assume an older
   `photoidentity_rehearsal_*` database is authoritative. Stop Photo Identity
   fully; leave PostgreSQL running.
2. From repo root in Windows PowerShell, replace just the verified database
   name below. Use a new backup rather than reusing the pre-v34 backup.

~~~powershell
$ErrorActionPreference = 'Stop'
$databaseName = 'REPLACE_WITH_CONFIRMED_APP_CONNECTED_DB'
$backupPath = Join-Path $env:LOCALAPPDATA (
  'PhotoIdentity\backups\postgresql\pre-wi0188-v35-' +
  (Get-Date -Format 'yyyyMMdd-HHmmss') + '.dump'
)
.\backup-postgres-catalogue.ps1 -DatabaseName $databaseName -OutputPath $backupPath
.\verify-postgres-backup-restore.ps1 -BackupPath $backupPath -ApplicationStopped

$restoreReportPath = "$backupPath.restore-verification.json"
$report = Get-Content -LiteralPath $restoreReportPath -Raw | ConvertFrom-Json
if (-not $report.passed -or [int]$report.restoredEvidence.schemaVersion -ne 34) {
    throw 'A verified isolated schema-v34 restore is mandatory before upgrade.'
}
~~~

3. Resolve the launcher-configured PostgreSQL connection variable into a
   temporary process-scoped name **without printing credentials**:

~~~powershell
$launcherCandidates = @(
  $env:PHOTOIDENTITY_LAUNCHER_CONFIG
  (Join-Path (Get-Location) 'PhotoIdentity.launcher.json')
  (Join-Path $env:LOCALAPPDATA 'PhotoIdentity\launcher.json')
)
$launcherPath = $launcherCandidates |
  Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
  Select-Object -First 1
if (-not $launcherPath) { throw 'Launcher configuration not found.' }
$launcher = Get-Content -LiteralPath $launcherPath -Raw | ConvertFrom-Json
$name = [string]$launcher.postgresConnectionEnvironmentVariable
if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
  throw 'Launcher PostgreSQL variable name missing or unsafe.'
}
$secret = @(
  [Environment]::GetEnvironmentVariable($name,'Process')
  [Environment]::GetEnvironmentVariable($name,'User')
  [Environment]::GetEnvironmentVariable($name,'Machine')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
if (-not $secret) { throw 'PostgreSQL credentials unavailable.' }
$env:PHOTOIDENTITY_WI0188_V35_REHEARSAL = $secret
Remove-Variable secret
try {
    dotnet run --project src/PhotoIdentity.Cli -c Release -- catalogue rehearse-v35 --postgres-connection-env PHOTOIDENTITY_WI0188_V35_REHEARSAL --restore-report $restoreReportPath
    if ($LASTEXITCODE -ne 0) { throw 'Isolated schema-v35 rehearsal failed.' }
}
finally {
    Remove-Item Env:\PHOTOIDENTITY_WI0188_V35_REHEARSAL -ErrorAction SilentlyContinue
}
~~~

Expected: `Schema v34 -> v35 rehearsal passed`, unchanged original
catalogue/photo/video/queue row counts, **exactly one new migration**,
video control row `enabled=false`, `paused=true`, `generation=0`,
and repeated schema initialization passed. Existing video job rows are
retained, and any lease from an earlier control generation cannot heartbeat
or acknowledge completion after a pause/resume cycle. The CLI selects ONLY the verified
local `photoidentity_restore_*` database from the report, never the launcher
database.

4. After rehearsal passes, deploy/restart the new package using your normal
   procedure. Require `/health` to report `schemaVersion: 35`, PostgreSQL
   ready, unchanged photo features and no video jobs executing.

## Read-only and controlled operations

The existing CLI supports:

~~~powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- video jobs status --postgres-connection-env $name --database $databaseName
dotnet run --project src/PhotoIdentity.Cli -c Release -- video jobs pause --postgres-connection-env $name --database $databaseName
dotnet run --project src/PhotoIdentity.Cli -c Release -- video jobs resume --postgres-connection-env $name --database $databaseName
dotnet run --project src/PhotoIdentity.Cli -c Release -- video jobs stop --postgres-connection-env $name --database $databaseName
~~~

The last three are **dry runs by default**. To actually persist operator
intent, repeat the chosen command with `--apply`. Pause vetoes new claims;
resume removes that pause but DOES NOT enable processing. Stop atomically
disables and pauses admission, cancels queued video jobs, and marks running
video jobs for cancellation. It never changes photo job rows.

There is **no automatic worker**, no kill signal sent to real subprocesses
by these commands, and no video execution toggle exposed. An eventual worker
must react to persisted pause/stop generation and active photo work by
terminating bounded local processes safely and cleaning unfinished files.

No credentials, database names, media paths or private job details should be
posted publicly; report aggregate PASS/FAIL only.

## Compatibility note

Older `catalogue rehearse-v33` and `rehearse-v34` command names remain
available for historical *isolated* v32 and v33 restores. Current source
initialization always advances through all newer migrations, including v35,
so do not use these older names to rehearse the maintained v34 database.
