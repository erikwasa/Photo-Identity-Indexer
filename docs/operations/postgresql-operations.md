# PostgreSQL production operations

PostgreSQL is the accepted single writable production catalogue for the maintainer installation. This runbook owns the WI-0106 day-to-day service, backup/restore, restart and upgrade boundary.

## Normal startup order

1. Start Podman Desktop / the Podman WSL machine if it is not already available.
2. From the repository checkout run `./verify-postgres.ps1`. This starts the repository Compose service unless `-SkipContainerStart` is supplied, verifies container authentication, Windows localhost forwarding, PostgreSQL protocol access and the live PostgreSQL acceptance tests.
3. Validate the private launcher configuration when it has changed:

   ```powershell
   powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
     -File .\Start-PhotoIdentity.ps1 `
     -ConfigurationPath "$env:LOCALAPPDATA\PhotoIdentity\launcher.json" `
     -ValidateConfigurationOnly
   ```

4. Start Photo Identity normally through the launcher.
5. Require `/health` to report `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and the current schema version before normal archive writes continue.

If Photo Identity exits during startup or fails to become healthy, inspect `%LOCALAPPDATA%\PhotoIdentity\launcher-logs\api.stderr.log` and run `./verify-postgres.ps1`. Do not change catalogue provider or point the application at a dynamic Podman-machine address merely to bypass a PostgreSQL/WSL failure.

## Persistent data and controlled shutdown

The Compose service mounts PostgreSQL 18 data at `/var/lib/postgresql` through the named Compose volume. The physical volume lives inside the Podman machine/WSL storage boundary; do not treat the VM-internal mount path as an operator backup format.

To inspect the actual volume attached to the running service without modifying it:

```powershell
Push-Location .\deploy\postgres
$container = (podman compose ps -q postgres).Trim()
podman inspect $container --format '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql"}}{{.Name}} -> {{.Destination}}{{end}}{{end}}'
Pop-Location
```

Normal shutdown order is:

1. stop/exit Photo Identity so no new catalogue writes are initiated;
2. from `deploy/postgres`, run `podman compose stop` when the database service itself should stop; and
3. allow Podman Desktop / WSL to stop normally.

`podman compose stop` and ordinary container recreation preserve the named volume. **Never run `podman compose down -v` against the production catalogue.** That command is retained only for explicitly disposable development databases.

## Restart/persistence acceptance

WI-0106 restart acceptance should prove that persisted production state survives service recreation rather than merely proving that a fresh container can start.

With Photo Identity stopped:

```powershell
Push-Location .\deploy\postgres
podman compose stop
podman compose up -d
Pop-Location

.\verify-postgres.ps1 -SkipContainerStart
```

Then start Photo Identity through the production launcher and verify `/health`, Archive status, a known Smart Collection and representative Review state. A PC restart can be accepted with the same sequence after Windows/Podman are back up.

The maintainer accepted the container/service restart half of this check on 2026-09-12: the service was stopped and started, `verify-postgres.ps1 -SkipContainerStart` passed, and normal Photo Identity use remained healthy afterward. Keep the combined work-item criterion open until an actual PC restart has also been observed.

## Routine logical backup

Use the repository backup wrapper rather than copying the Podman volume or PostgreSQL data directory:

```powershell
.\backup-postgres-catalogue.ps1
```

If multiple migrated/rehearsal databases remain in the server, select the accepted production authority explicitly:

```powershell
.\backup-postgres-catalogue.ps1 -DatabaseName <production-database-name>
```

Do not inspect PostgreSQL's physical data files to decide which database is production. Database names and server activity are the supported operational level; PostgreSQL's internal relation files and OID directories are not a safe authority-identification mechanism.

### Windows streaming backup (avoids Podman dump staging)

To back up the confirmed production catalogue without writing a temporary dump into
the Podman virtual disk, use the streaming wrapper:

```powershell
.\backup-postgres-catalogue-improved.ps1 `
    -DatabaseName 'photoidentity_main'
```

The script discovers the running PostgreSQL container through this checkout's
`deploy/postgres` Compose service. Use `-ContainerName` only when intentionally
targeting a different already-verified container. It refuses missing catalogue
markers, existing backup/report paths, and a OneDrive destination identical to
the local backup. By default it requires at least **4 GiB free** on each
destination volume before starting (`-MinimumFreeGiB` can override the minimum).
The compressed archive size is not predictable: check sufficient Windows space
for the **whole** backup; with `-CopyToOneDrive` budget for a second complete
copy. The script rechecks the available OneDrive-copy space against the actual
finished archive size.

For an optional copy to the signed-in OneDrive sync directory, add
`-CopyToOneDrive` (and optionally `-OneDriveDirectory 'D:\Path\To\OneDrive\DatabaseBackups'`).
This copies files to a **local** sync directory, not directly to OneDrive's cloud.
Verify that the backup appears in the OneDrive web UI and independently download
and SHA-256-check the remote bytes before treating it as an independent recovery
checkpoint; see [backup-file retention](#operator-managed-backup-file-retention-wi0209).
The `.dump` contains private data and is not encrypted by the script.

The streaming wrapper checks the complete PostgreSQL custom-archive decode,
SHA-256 and (if requested) the local copy's digest. **It does not perform a
real database restore.** Its manifest explicitly says
`restoreTestPerformed=false`. To qualify as a protected recovery checkpoint,
stop Photo Identity *before* taking the backup and keep it stopped while running
the isolated restore verifier below. Unlike streaming, that full verifier still
stages the dump in Podman and creates a temporary restored database; reserve
approximately another full database's worth of Podman storage. A normal live
`pg_dump` is transactionally consistent if a full row-count comparison is not
needed.

Synthetic Windows tests (no Podman or production data):

```powershell
pwsh -NoProfile -File .\tests\PowerShell\postgres-streaming-backup.tests.ps1
```

The currently maintained streamed-backup implementation is a supervised
operator path until a real Windows/Podman backup and restore acceptance run
has been recorded. Interrupted runs may leave an exact
`*.dump.partial-<guid>` file; inspect the specific file before removing it.
Do not use wildcard cleanup in the backup directory.

### Identifying the active production database when rehearsals remain

When several retained rehearsal databases all contain valid Photo Identity schema markers, identify the active catalogue from PostgreSQL activity rather than guessing from the timestamped names.

With Photo Identity running, capture aggregate database activity:

```powershell
Push-Location .\deploy\postgres
$container = (podman compose ps -q postgres).Trim()
podman exec $container sh -lc 'psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT datname AS database, xact_commit, tup_returned, tup_fetched, tup_inserted, tup_updated, tup_deleted, stats_reset FROM pg_stat_database WHERE datname NOT IN (''template0'', ''template1'') ORDER BY datname;"'
Pop-Location
```

Use Photo Identity normally for a short observation window (for example Review, Archive and a Smart Collection), then run the same query again. The production catalogue should show a materially larger increase in transaction/read counters than untouched rehearsal copies. Stop Photo Identity before using the identified database for WI-0106 backup/restore acceptance.

On the maintainer installation this method identified the accepted active authority unambiguously after direct launcher-derived name resolution proved unreliable with the private environment-variable value shape. The operational backup then used explicit `-DatabaseName`; no connection string or credential needed to be printed or stored in documentation.

By default backups are written beneath `%LOCALAPPDATA%\PhotoIdentity\backups\postgresql`. The backup is a `pg_dump` custom-format file copied byte-for-byte from the container, plus a JSON report containing its SHA-256 and source database name. Neither file contains the PostgreSQL password or connection string, but the dump contains the private catalogue and must be protected as sensitive local data.

A normal logical `pg_dump` is transactionally consistent and may be taken while the application is running. For the WI-0106 restore acceptance below, stop the application first and take a fresh backup so exact row counts can be compared deterministically with the restored copy.

## Operator-managed backup-file retention (WI-0209)

The normal backup tool continues to produce a dump and SHA-256 manifest without automatic retention or removal. The explicitly invoked `manage-postgres-backups.ps1` supports read-only inventory, registration of short-lived pre-change precautions, acceptance-controlled exact-file cleanup, and a protected restore-verified recovery checkpoint with manually confirmed independent off-disk availability. **Do not infer remote OneDrive durability from local sync status.** This workflow never deletes unclassified backups, the protected checkpoint, remote objects or any PostgreSQL database.

The procedure is explicit and local: no scheduled rotation, OneDrive API, cloud transfer, or backup deletion based on age or on an isolated restore succeeding. (Restore verification below manages temporary restored **databases**, not backup files.)

### Approved local root and read-only inventory

Run from PowerShell 7 at the repository root:

~~~powershell
$backupRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\backups\postgresql'
.\manage-postgres-backups.ps1 | Format-Table Name, Role, MiB, ManifestPresent, RestoreReportPresent, ReparsePoint
~~~

For an **individual already-existing backup**, inspect its *actual bytes* and restore report without changing its classification or retention state:

~~~powershell
$candidate = Join-Path $backupRoot '<existing-backup>.dump'
.\manage-postgres-backups.ps1 -InspectBackup -BackupPath $candidate | Format-List
~~~

The inspection checks the dump hash/size, original manifest and any restore report. `EligibleForCheckpointPromotion=true` establishes only **local restore evidence**; it does not establish that an independent remote copy exists. `RemoteConfirmationRecorded=true` means an operator previously registered an attestation, **not** a live check of OneDrive availability. Promotion still requires independently retrieving and hashing the off-disk copy. A mismatched manifest or restore fingerprint causes inspection to fail.

The inventory does not change files. Previously existing dumps are **unclassified** until individually registered; unclassified backups are never eligible for cleanup. The tool requires direct children of the approved backup directory, rejecting nested paths, traversal, links/junctions and reparse files (including online-only placeholders). Explicit `-BackupRoot` is for intentionally approved alternate directories; review that scope before using it.

The only state file is local **retention-state.json** beside the backup files; it records pre-change, protected recovery checkpoint and superseded checkpoint roles. Never publish this private file, a dump, personal identifiers or remote evidence to the repository. Do not manually edit registry roles.

### A. Pre-change precaution and acceptance-controlled retirement

Identify the actual production catalogue before taking a precautionary backup. Stop the app if exact count-stable restore checks are required.

~~~powershell
$backup = Join-Path $backupRoot 'prechange-maintenance.dump'
.\backup-postgres-catalogue.ps1 -DatabaseName '<confirmed-production-database>' -OutputPath $backup
$sha = (Get-Content -LiteralPath "$backup.json" -Raw | ConvertFrom-Json).backupSha256
.\manage-postgres-backups.ps1 -RegisterPreChange -BackupPath $backup -ExpectedSha256 $sha -ChangeReference 'your-specific-change'
~~~

A passing backup or isolated restore **does not establish that the subsequent database/application change succeeded**. Keep the pre-change backup for failures, interruptions and unverified changes. Before removal, review a **read-only exact-file plan**; it validates the selected backup and lists only the files that would be targeted, without deleting anything:

~~~powershell
.\manage-postgres-backups.ps1 -PlanRemoval -BackupPath $backup -ExpectedSha256 $sha | Format-List
~~~

Only after the actual post-change tests have passed:

~~~powershell
.\manage-postgres-backups.ps1 -RemovePreChange -BackupPath $backup -ExpectedSha256 $sha -ConfirmChangeAccepted -ChangeAcceptanceEvidence 'passed-post-change-verification-reference' -ConfirmRemoval
~~~

This verifies the exact dump SHA-256 and manifest before deleting its dump and only its related JSON manifest and optional restore-verification sidecar. No wildcard, automatic age-based deletion or implicit acceptance. Failures report an incomplete operation and retain the classification for investigation. If a sidecar was already deleted during a failed operation, manual recovery may be needed; never use a wildcard retry.

### B. One protected recovery checkpoint and replacement

A candidate recovery checkpoint must have a matching SHA-256 manifest and a **passing isolated restore verification** whose temporary database cleanup succeeded. The same verified pre-change dump may be promoted rather than making another dump.

Confirm that an **off-disk** copy exists and is usable, preferably through the signed-in **OneDrive web UI**. Inspect the actual remote item, retrieve it independently of the local sync folder, and calculate SHA-256 on those retrieved bytes. A OneDrive sync icon or local OneDrive-synced directory alone is **not** evidence of another physical copy. Record remote availability and hash-check details in a private operator log.

~~~powershell
$localSha = (Get-Content -LiteralPath "$backup.json" -Raw | ConvertFrom-Json).backupSha256
$remoteSha = '<SHA-256-of-independently-retrieved-remote-file>'
.\manage-postgres-backups.ps1 -SetRecoveryCheckpoint -BackupPath $backup -ExpectedSha256 $localSha -RemoteSha256 $remoteSha -RemoteConfirmation 'OneDrive web copy independently retrieved and checked; see private operator log' -ConfirmIndependentCopy
~~~

The tool verifies local hash, manifest, successful restore report, supplied remote SHA-256 and **explicit operator attestation**. It never accesses OneDrive APIs or claims to have verified off-device availability itself. If independent confirmation fails, do not promote or retire anything.

To replace a checkpoint, create and fully verify a separately named backup, confirm its remote copy, then repeat `-SetRecoveryCheckpoint` using the new file. The former checkpoint is **marked superseded but not deleted**. Only when the replacement remains verified and protected:

~~~powershell
.\manage-postgres-backups.ps1 | Format-Table Name, Role, ManifestPresent, RestoreReportPresent
.\manage-postgres-backups.ps1 -RemoveSuperseded -BackupPath $oldBackup -ExpectedSha256 $oldSha -ConfirmRemoval
~~~

Removal rechecks the **current** checkpoint's local hash, manifest and passing restore report. If any evidence is missing, the former checkpoint is retained. The active checkpoint can never be deleted using either cleanup mode. This tool deletes only local approved files, **not remote recovery copies**; do not manually delete the only independent cloud copy.

The helper rejects unknown files, hashes, missing manifests, failed restore reports, incomplete remote confirmation, reparse-point paths, missing post-change acceptance, and attempts to remove the sole recovery checkpoint. Synthetic tests: `pwsh -NoProfile -File .\tests\PowerShell\postgres-backup-retention.tests.ps1` (CI runs it). The helper never modifies PostgreSQL production data, legacy restore databases, OneDrive objects, Podman volumes or the Windows VHDX. Accepted on the maintained host on 2026-11-01 (WI-0209), with one operator-attested recovery checkpoint registered; that attestation is not continuous cloud monitoring.

## Isolated restore verification

For acceptance, stop Photo Identity first. Then create a fresh backup while the catalogue is quiescent:

```powershell
.\backup-postgres-catalogue.ps1 -DatabaseName <production-database-name>
```

Run the verifier against the new `.dump` file:

```powershell
.\verify-postgres-backup-restore.ps1 `
  -BackupPath "<path-to-new-backup.dump>" `
  -ApplicationStopped
```

The verifier:

1. checks the backup SHA-256 against its JSON report;
2. rejects a still-running Photo Identity process;
3. captures the production schema version, public-table set, exact row counts and unvalidated-constraint count;
4. creates a uniquely named isolated PostgreSQL database;
5. restores the custom-format backup there with `pg_restore --single-transaction`;
6. compares the complete public-table set and exact row counts with the stopped source; and
7. writes a durable restore-verification JSON report **before** any cleanup;
8. unless explicitly retained, validates its exact generated name and database ownership marker, confirms no sessions are connected, removes **only its own** successful isolated restore, and publishes the final cleanup outcome.

A passing fresh run normally reports `passed=true`, `cleanupStatus=removed`, and `cleanupRequired=false`. The backup `.dump`, SHA-256 manifest and comparison JSON remain intact. Restore failures, comparison failures, interrupted runs and report-publishing failures retain the isolated database for investigation. Any cleanup failure is reported and causes the verifier to fail even when `passed=true`; do not treat `passed` alone as proof that storage was reclaimed.

**Maintained Windows acceptance (WI-0208, 2026-11-01):** A verified fresh restore of the full-size production catalogue passed its comparisons and then automatically removed its exact owned temporary database. PostgreSQL autovacuum workers briefly blocked the normal cleanup window; the verifier's bounded, autovacuum-only extension allowed maintenance to finish before a non-force drop. Client/mixed/unknown sessions, ownership mismatches and excessive maintenance still fail closed. The unmarked legacy restored database remains protected. The acceptance log did not include paired Podman-volume/VHDX measurements. See [Full restore acceptance run](#full-restore-acceptance-run) to repeat it.

### Maintenance-aware automatic cleanup

For a *fresh successful* verification, the ordinary short grace is 11 checks separated by 2 seconds. If the sessions at its end are **exclusively autovacuum workers**, automatic cleanup may continue for at most **60 additional checks at 10-second intervals** (plus diagnostic-query time). The verifier revalidates the exact database name, matching ownership marker and zero sessions on each check before an **ordinary non-force** `dropdb`. Other client/backend types, missing diagnostics, changed provenance and persistent maintenance beyond the bound fail safely. PostgreSQL 18 autovacuum is cluster-managed, so **do not disable autovacuum for production** or rely on `ALLOW_CONNECTIONS false` to exclude maintenance workers.

The verifier records an aggregate diagnostic snapshot **for every busy cleanup attempt** in the same `*.restore-verification.json`: backend type, recognized client kind (`psql`, `pg_restore`, unnamed or other), activity state, and the number of ongoing `ANALYZE` and `VACUUM` operations. No backend PID, user, SQL statement, media, address or original application name is included. Inspect them for one run:

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

Classify the result before changing code:

- **Only `autovacuum worker` plus active `ANALYZE` or `VACUUM`**: PostgreSQL maintenance, not an application-client leak, blocked cleanup. Do not terminate maintenance workers.
- **`client backend` / `psql` or `pg_restore`**: examine verifier connection lifecycle; continued use of those clients after completion indicates a process-lifetime problem.
- **`client backend` / `other-client` or `unnamed`**: investigate another client connecting to the generated database.
- **An internal worker other than autovacuum**: investigate that worker class before deciding whether cleanup should wait.
- **Differences between `sessionsAtOwnershipCheck` and activity totals**: these are separate read-only snapshots; compare timestamps, not a single count.

If cleanup failed, **do not create another full restore**. The verifier also writes a last-chance `cleanupSessionSummary` on failure. Recheck live sessions read-only; after the database becomes idle and its marker is verified, use only the exact-name removal below. Do not rewrite a failed report to claim it succeeded.

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

No `--force`, process termination, bulk cleanup or online VHDX compaction is permitted.

For a subsequent migration/schema rehearsal that **needs the actual restored database**, opt in to retention:

~~~powershell
.\verify-postgres-backup-restore.ps1 -BackupPath $backupPath -ApplicationStopped -RetainRestoredDatabase
~~~

The report then records `cleanupStatus=retained` and `cleanupRequired=true`. Rehearsal runbooks must use this switch, and then retire their retained restore with the protected inventory command below. A read-only `-ResumeRestoredDatabase` invocation always retains the copy after verification so it remains available for explicit recovery/disposal. A fresh invocation now refuses to create a second owned restore for the **same backup SHA-256** while an earlier owned copy remains; resume or explicitly retire the existing copy first.

### Resume a retained restore after verification fails

If the script prints **“Restoring the backup into the isolated database...”** and then fails while collecting PostgreSQL evidence, the `pg_restore` command may have completed successfully even though **verification has not passed**. Preserve the original `.dump`, JSON report and the restored database. Do not repeat the backup or choose a different source database until the actual query error is known.

A normal rerun creates another database. With the exact generated name printed in the original attempt, resume **read-only restore verification** of the existing copy instead:

~~~powershell
.\verify-postgres-backup-restore.ps1 `
  -BackupPath $backupPath `
  -ApplicationStopped `
  -VerificationDatabaseName 'photoidentity_restore_YYYYMMDDHHMMSS_xxxxxx' `
  -ResumeRestoredDatabase
~~~

Replace that placeholder with the **exact** name from the previous attempt. The resume switch refuses other name formats, and it does not call `createdb`, copy a dump or call `pg_restore`. It rechecks the backup fingerprint and original source evidence, then compares the retained restored database's version, all public table counts and constraints. SQL query failures now identify the database, the failed read-only SQL query and a short PostgreSQL error message instead of a generic failure. This does not automatically repair a bad restore. If it fails, keep the restored database for diagnosis and do not apply a schema migration.

**Safety:** Keep Photo Identity stopped during count-stable restore verification. The verifier cannot by itself establish that a backup was taken from the current maintained production authority; confirm the selected source from the launcher or database activity before using this as a production migration gate.

### Inspect and explicitly retire retained restores

List exact generated restore names, size, active sessions and ownership without modifying anything:

~~~powershell
.\manage-postgres-restores.ps1 | Format-Table Name, SizeGiB, Sessions, Ownership
~~~

Only post-WI-0208 copies with a database ownership marker (`owned`) can be retired by the tool. **Older, unmarked legacy copies are inventory-only:** a matching name or timestamp alone is not proof of ownership. Investigate their source/role with the PostgreSQL administrator; do not bypass the safety gate with a wildcard, `--force`, direct arbitrary `dropdb` or a volume reset.

When a retained/recovered copy is genuinely disposable, use its **exact** report values and confirm the active production database separately from the launcher or observed activity:

~~~powershell
$report = Get-Content -LiteralPath "$backupPath.restore-verification.json" -Raw | ConvertFrom-Json
$productionDb = '<confirmed-current-production-database>'
.\manage-postgres-restores.ps1 `
  -RemoveDatabase $report.verificationDatabase `
  -ExpectedMarker $report.restoreOwnershipMarker `
  -ProductionDatabaseName $productionDb `
  -ConfirmRemoval
~~~

The command refuses missing/mismatched markers, production-name matches, active sessions and non-generated names. It never terminates connections. If the stored report was overwritten by a later check, inventory displays the current ownership marker; re-establish that the exact copy is disposable before any confirmed removal. On any ambiguity, do nothing.

### Windows/Podman storage behavior

Dropping a restored database releases PostgreSQL-managed space inside the Podman VM. **Windows free space need not increase** because the WSL/Podman VHDX may retain its allocated size. For maintenance evidence, record SQL sizes (`pg_database_size` and inventory) and separately measure Windows volume free bytes before/after. Optional VHDX compaction requires a verified recovery backup, a fully stopped Photo Identity/PostgreSQL/Podman machine, an offline/detached virtual disk and a supported Windows disk-management method; never compact a mounted live VHDX or delete the production Podman volume. This is not part of routine verification and is not automated by WI-0208.

### Full restore acceptance run

Use this to repeat the maintained-host restore acceptance. It does **not** delete backup files or production data. It runs one complete restore and may temporarily use about the catalogue's size inside the Podman virtual disk; confirm there is room first and never compact the VHDX to make room for a running restore.

1. Identify the **actual** active production database from the launcher/connection or observed activity (see above). Never select by a familiar name alone.
2. Stop Photo Identity and verify it has exited. Leave the PostgreSQL Compose service running. Confirm a safe recovery backup exists independently of the disk being exercised.
3. Run the source-free tests:

~~~powershell
pwsh -NoProfile -File .\tests\PowerShell\postgres-restore-lifecycle.tests.ps1
pwsh -NoProfile -File .\tests\PowerShell\postgres-restore-verifier.integration.ps1
~~~

Optionally, with Photo Identity stopped, run the disposable real-PostgreSQL test. It creates a **new, randomly named Compose project and volume** with a tiny synthetic database (random high localhost port, random password), exercises cleanup, retention, duplicate retry, read-only resume, confirmed disposal and a failing row-count comparison, and removes the project when done. If Podman lacks storage, skip it and report it as unverified rather than using production to simulate failure.

~~~powershell
pwsh -NoProfile -File .\tests\PowerShell\postgres-restore-disposable.integration.ps1 -ConfirmDisposableService
~~~

Record the before state without modifying any database:

~~~powershell
$before = @(.\manage-postgres-restores.ps1)
$before | Format-Table Name, SizeGiB, Sessions, Ownership
$windowsFreeBeforeGiB = [math]::Round((Get-PSDrive -Name C).Free / 1GB, 2)
Push-Location .\deploy\postgres
podman compose ps
podman system df
Pop-Location
~~~

Run one restore from a fresh backup taken while the application is stopped, and check the outcome:

~~~powershell
$productionDb = '<confirm-active-production-database>'
.\backup-postgres-catalogue.ps1 -DatabaseName $productionDb

# Use the actual .dump path just printed by the wrapper.
$backupPath = '<exact-path-to-new-backup.dump>'
.\verify-postgres-backup-restore.ps1 -BackupPath $backupPath -ApplicationStopped

$report = Get-Content -LiteralPath "$backupPath.restore-verification.json" -Raw | ConvertFrom-Json
$report | Select-Object passed, cleanupStatus, cleanupRequired, verificationDatabase, sourceDatabaseBytes, restoredDatabaseBytes
if (-not $report.passed -or $report.cleanupStatus -ne 'removed' -or $report.cleanupRequired) {
    throw 'Restore success/cleanup acceptance did not pass.'
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

Record the after state separately; Windows free space may **not** increase (see storage behavior above):

~~~powershell
$after | Format-Table Name, SizeGiB, Sessions, Ownership
$windowsFreeAfterGiB = [math]::Round((Get-PSDrive -Name C).Free / 1GB, 2)
Push-Location .\deploy\postgres
podman system df
Pop-Location
~~~

Testing explicit retention creates **another full restored database**; run it only when capacity permits. Use `-RetainRestoredDatabase`, confirm `cleanupStatus=retained` and `cleanupRequired=true`, then remove that copy with the exact-name `manage-postgres-restores.ps1 -RemoveDatabase ... -ConfirmRemoval` command above. For simulated restore, comparison, report-write and cleanup failures, rely on the synthetic CI tests; never force failures against production.

Report: the synthetic test results; backup/hash/row-comparison success; `passed` and `cleanupStatus`; absence from inventory; source/restore sizes; Podman disk/volume observations; Windows free space before/after; and whether retention was tested or skipped. Do **not** share dump contents, private catalogue paths, connection strings, usernames, identities or photos.

The maintainer completed this acceptance successfully on 2026-09-12 from a stopped production source. The custom-format dump was hash-recorded, restored into an isolated database, and passed schema/table/count/constraint comparison before that isolated database was explicitly removed.

M24/WI-0106 stabilization is complete. Recovery always uses a verified PostgreSQL logical backup.

## PostgreSQL 18 service upgrades

The Compose definition intentionally stays on PostgreSQL major version 18. Before changing the image used by production, create a fresh logical backup and periodically prove isolated restore.

For an update that remains within PostgreSQL 18:

```powershell
Push-Location .\deploy\postgres
podman compose pull postgres
podman compose up -d
Pop-Location

.\verify-postgres.ps1 -SkipContainerStart
```

Then start Photo Identity and verify `/health` and representative catalogue state.

Do **not** point a new PostgreSQL major version at the existing PostgreSQL 18 data volume. A future major-version upgrade must be treated as a separate migration: verified logical backup, separate target storage/service, restore, representative application verification and controlled authority switch.

## Failure recovery

Use `/health` and `./verify-postgres.ps1` to classify failures before changing configuration:

- `unavailable`: verify Podman/WSL service state and localhost forwarding;
- `authentication_failed`: the persisted cluster credentials and private configuration disagree;
- `migration_failed`: PostgreSQL is reachable but catalogue initialization/migration failed; preserve the database and diagnose before retrying writes;
- launcher startup timeout/exit: inspect launcher stderr, then verify PostgreSQL independently.

Do not use a destructive volume reset for a production authentication or migration failure. Preserve the cluster and use the latest verified logical backup when a genuine restore is required.

### Unexpected PostgreSQL disconnect during normal operation

If application logs show an existing PostgreSQL socket being forcibly closed and then new connections to `127.0.0.1:5432` being actively refused, preserve the container evidence before restarting components:

```powershell
Push-Location .\deploy\postgres
podman compose ps -a
$container = (podman compose ps -a -q postgres).Trim()
podman inspect $container --format '{{json .State}}'
podman inspect $container --format '{{.RestartCount}}'
podman logs --since 4h $container
Pop-Location
```

If `podman compose ps` itself fails with `unable to connect to Podman socket` / `connection actively refused`, the failure is above PostgreSQL: the Podman machine/control plane is unavailable. Capture the local runtime view before recovery:

```powershell
podman system connection list
podman machine list
wsl --list --verbose
```

Then start the existing Podman machine rather than initializing a new one. Once Podman is reachable again, run `./verify-postgres.ps1` from the repository root; it will bring up the existing Compose service as needed and verify the persisted catalogue path. Only after the machine is reachable can container state, restart count and PostgreSQL logs distinguish whether the database container itself also restarted. The Compose `restart: unless-stopped` policy cannot recover anything while the Podman machine/runtime itself is unavailable.

The Compose service is configured with `restart: unless-stopped`, so a database/container failure may already have recovered by the time the application crash is noticed. Container state, restart count and PostgreSQL logs distinguish a server restart from an application-only fault and can expose shutdown/recovery/fatal-server messages. If the container did not restart and the PostgreSQL log has no interruption, investigate Podman/WSL localhost forwarding next.

Long-lived background workers should treat a brief catalogue interruption as retryable at the worker boundary rather than letting one exception stop the entire API. Archive advancement and automatic place enrichment already follow that rule; WI-0106 extends it to identity-match regeneration after the sustained catch-up run exposed the missing boundary. Retrying the application worker does not replace root-cause investigation when PostgreSQL/Podman interruptions recur.

## Sustained archive catch-up acceptance

Backup/restore and container restart acceptance are complete enough to exercise the next WI-0106 phase, but sustained catch-up is not yet accepted. The first PostgreSQL-backed run made healthy progress; a later longer run then hit the unexpected PostgreSQL disconnect described above and the API stopped because identity-match regeneration lacked a worker-level retry boundary. Diagnose the service interruption, merge the resilience correction, then rerun **Advance archive** long enough to prove continued progress across the same operational window.

Use these privacy-safe operational endpoints while the run is active:

```powershell
$Api = "http://127.0.0.1:5080"
Invoke-RestMethod "$Api/health"
Invoke-RestMethod "$Api/api/archive/status"
Invoke-RestMethod "$Api/api/archive/storage"
Invoke-RestMethod "$Api/api/archive/diagnostics/throughput"
```

The acceptance question is operational stability. Watch that backlog/progress continues to move, managed hydration is released normally, PostgreSQL health remains ready, and diagnostics expose enough aggregate stage/counter evidence to investigate a stall without enabling per-photo tracing.

After sustained catch-up is accepted, add a small real source increment and run the normal synchronization/advance path without full regeneration. Verify that the new photos synchronize, analyze, enrich and appear for review while unchanged existing work is not unnecessarily repeated. Record that as the final daily-style increment evidence for WI-0106.
