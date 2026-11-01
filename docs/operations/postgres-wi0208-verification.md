# WI-0208 maintained Windows / Podman acceptance

Run from a PowerShell 7 window in the repository root after the PR is merged. This procedure does **not** delete backup files or production data. It exercises one actual, complete restore and may temporarily consume approximately the size of the catalogue inside the Podman virtual disk. Confirm there is adequate space before running; do not use Windows VHDX compaction to make room for a still-running restore.

## 1. Preconditions and baseline

1. Identify the **actual** active production database from the launcher/connection or observed database activity as described in [PostgreSQL operations](postgresql-operations.md). Never select by a familiar name alone.
2. Stop Photo Identity and verify it has exited. Leave the PostgreSQL Compose service running. Confirm a safe recovery backup exists independently of the disk being exercised.
3. Run the source-free Windows tests first:

~~~powershell
pwsh -NoProfile -File .\tests\PowerShell\postgres-restore-lifecycle.tests.ps1
pwsh -NoProfile -File .\tests\PowerShell\postgres-restore-verifier.integration.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
~~~

Record the before state without modifying any databases:

~~~powershell
$before = @(.\manage-postgres-restores.ps1)
$before | Format-Table Name, SizeGiB, Sessions, Ownership
$beforeNames = @($before | ForEach-Object Name)
$windowsFreeBeforeGiB = [math]::Round((Get-PSDrive -Name C).Free / 1GB, 2)
Push-Location .\deploy\postgres
podman compose ps
podman system df
Pop-Location
~~~

Unmarked older restore copies are **not** automatically eligible for deletion.

### Optional disposable real PostgreSQL test (before the production check)

The following script creates a **new, randomly named Compose project and volume** with a tiny synthetic database; it never reuses the production project. It exercises successful cleanup, explicit retention, a duplicate retry, read-only resume, confirmed disposal and a deliberately failing row-count comparison. The generated disposable project/volume is removed when the test ends. Run it only with the explicit opt-in, while Photo Identity is stopped:

~~~powershell
pwsh -NoProfile -File .\tests\PowerShell\postgres-restore-disposable.integration.ps1 -ConfirmDisposableService
~~~

The temporary project binds a random high localhost port, uses a random password, and reports an explicit warning if its isolated Compose cleanup fails. If Podman has insufficient free storage or cannot start another service, skip and report this gate as unverified rather than using production to simulate failure.

## 2. Successful real restore (one representative run)

Use a fresh backup taken while the application is stopped; otherwise a stale backup can disagree with the current source's exact table counts.

~~~powershell
$productionDb = '<confirm-active-production-database>'
.\backup-postgres-catalogue.ps1 -DatabaseName $productionDb

# Use the actual .dump path just printed by the wrapper.
$backupPath = '<exact-path-to-new-backup.dump>'
.\verify-postgres-backup-restore.ps1 -BackupPath $backupPath -ApplicationStopped

$report = Get-Content -LiteralPath "$backupPath.restore-verification.json" -Raw | ConvertFrom-Json
$report | Select-Object passed, cleanupStatus, cleanupRequired, verificationDatabase, sourceDatabaseBytes, restoredDatabaseBytes
if (-not $report.passed -or $report.cleanupStatus -ne 'removed' -or $report.cleanupRequired) {
    throw 'WI-0208 success/cleanup acceptance did not pass.'
}
$after = @(.\manage-postgres-restores.ps1)
if (@($after | Where-Object Name -eq $report.verificationDatabase).Count -ne 0) {
    throw 'A supposedly removed restored database is still in inventory.'
}
if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath "$backupPath.json" -PathType Leaf)) {
    throw 'The source backup or hash evidence was unexpectedly removed.'
}
~~~


The verifier makes 11 bounded checks at two-second intervals. Any failed cleanup leaves its owned verification copy intact, with `passed=true`, `cleanupStatus=failed` and `cleanupRequired=true`. It never terminates connections or uses force. Both earlier full-size runs passed the integrity comparison but had busy sessions through the entire cleanup window; the sessions later disappeared without operator intervention. The backend category during those busy checks was not captured, so **do not infer the cause from their later disappearance**.

### One consolidated diagnosis instead of repeated trial restores

**Only once the follow-up diagnostics PR and CI are accepted**, perform one real restore from a fresh stopped-source backup using the commands in section 2. The verifier now captures an aggregate diagnostic snapshot **for every busy cleanup attempt**, including backend type, recognized client kind (`psql`, `pg_restore`, unnamed or other), activity state, and the number of ongoing `ANALYZE` and `VACUUM` operations. Each sample is recorded in the same durable `*.restore-verification.json`, whether cleanup eventually succeeds or fails. Categories are deliberately limited; no backend PID, user, SQL statement, media, address or original application name is included.

Inspect all samples from **that one run**:

~~~powershell
$report = Get-Content -LiteralPath "$backupPath.restore-verification.json" -Raw | ConvertFrom-Json
$report | Select-Object passed, cleanupStatus, cleanupRequired, verificationDatabase

foreach ($sample in @($report.cleanupBusyDiagnostics)) {
    if ($sample.diagnosticUnavailable) {
        [pscustomobject]@{ Attempt = $sample.attempt; ObservedSessions = $sample.sessionsAtOwnershipCheck; Diagnostic = 'unavailable' }
        continue
    }
    [pscustomobject]@{
        Attempt = $sample.attempt
        TimestampUtc = $sample.snapshot.capturedAtUtc
        ObservedSessions = $sample.sessionsAtOwnershipCheck
        AnalyzeRunning = $sample.snapshot.analyzeRunning
        VacuumRunning = $sample.snapshot.vacuumRunning
    }
    $sample.snapshot.activity | Format-Table backendType, clientKind, state, sessions
}
~~~

Classify the result before making any further code changes:

- **Only `autovacuum worker` plus active `ANALYZE` or `VACUUM`**: PostgreSQL maintenance, not an application-client leak, blocked cleanup. Design a bounded maintenance-aware cleanup or scheduling policy; do not terminate maintenance workers.
- **`client backend` / `psql` or `pg_restore`**: examine verifier connection lifecycle; repeat use of those clients after completion would indicate a process-lifetime problem.
- **`client backend` / `other-client` or `unnamed`**: investigate another client connecting to the generated database rather than assuming PostgreSQL maintenance.
- **An internal worker other than autovacuum**: investigate that worker class before deciding whether cleanup should wait.
- **Differences between `sessionsAtOwnershipCheck` and activity totals**: these are separate read-only snapshots and may differ as connections come/go; inspect the timestamps, not just a single count.

If cleanup failed, **do not create another full restore**. The verifier also writes a last-chance `cleanupSessionSummary` on failure. Recheck live sessions with the read-only query below; after the database becomes idle and its marker is verified, use only the exact-name manual cleanup documented in PostgreSQL operations. Do not rewrite the previous failed report to claim it succeeded.

~~~powershell
$restoreDb = [string]$report.verificationDatabase
if ($restoreDb -cnotmatch '^photoidentity_restore_[0-9]{14}_[a-f0-9]{6}$') {
    throw 'Unexpected restore database name.'
}
Push-Location .\deploy\postgres
try {
    $container = (podman compose ps -q postgres).Trim()
    $sql = "SELECT backend_type, COALESCE(state, 'n/a') AS state, COUNT(*) AS sessions FROM pg_stat_activity WHERE datname = '$restoreDb' GROUP BY backend_type, state ORDER BY backend_type, state;"
    podman exec -e "CHECK_SQL=$sql" $container sh -lc 'psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -c "$CHECK_SQL"'
}
finally {
    Pop-Location
}
~~~

This inspection is read-only. If zero sessions remain, manual removal must still validate the exact generated name, marker, and production exclusion. Legacy unmarked restore copies remain inventory-only. No `--force`, process termination, bulk cleanup or online VHDX compaction is permitted.


Record the after state separately. Windows disk free space may **not** increase when PostgreSQL frees space inside a previously grown WSL/Podman VHDX.

~~~powershell
$after | Format-Table Name, SizeGiB, Sessions, Ownership
$windowsFreeAfterGiB = [math]::Round((Get-PSDrive -Name C).Free / 1GB, 2)
Push-Location .\deploy\postgres
podman system df
Pop-Location
[pscustomobject]@{
    RestoredDatabaseRemoved = $report.cleanupStatus -eq 'removed'
    WindowsFreeBeforeGiB = $windowsFreeBeforeGiB
    WindowsFreeAfterGiB = $windowsFreeAfterGiB
}
~~~

## 3. Optional explicit retention / safe removal

This creates **another full restored database**, so run only when capacity permits (or against a small disposable PostgreSQL source fixture). To test the full retain/removal boundary with the same fresh backup:

~~~powershell
.\verify-postgres-backup-restore.ps1 -BackupPath $backupPath -ApplicationStopped -RetainRestoredDatabase
$report = Get-Content -LiteralPath "$backupPath.restore-verification.json" -Raw | ConvertFrom-Json
if ($report.cleanupStatus -ne 'retained' -or -not $report.cleanupRequired) {
    throw 'Explicit retention was not honored.'
}
.\manage-postgres-restores.ps1 | Format-Table Name, SizeGiB, Sessions, Ownership
# Only after confirming this is the disposable retained test copy:
.\manage-postgres-restores.ps1 -RemoveDatabase $report.verificationDatabase -ExpectedMarker $report.restoreOwnershipMarker -ProductionDatabaseName $productionDb -ConfirmRemoval
~~~

For simulated restore, comparison, report-write, cleanup failure and read-only resume behavior, rely on the synthetic CI test suite first; use disposable test databases for additional real destructive-path testing, never force deliberate failures against production.

## 4. Acceptance results to report

Record the CI/synthetic test result; real backup/hash/row-comparison success; `passed` and `cleanupStatus`; name absence in inventory; source/restore sizes; Podman disk/volume observations; Windows free space before/after; and whether explicit retention/removal was tested or skipped for capacity. Do **not** share actual dump contents, private catalogue paths, connection strings, usernames, identities or photos.

No VHDX compaction is required to accept automatic restored-database cleanup. Existing unmarked copies are not an excuse for wildcard deletion or termination of running PostgreSQL sessions. WI-0209 separately handles when to retire local backup **files**.
