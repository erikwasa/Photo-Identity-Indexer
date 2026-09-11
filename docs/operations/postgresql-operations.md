# PostgreSQL production operations

PostgreSQL is the accepted single writable production catalogue for the maintainer installation. This runbook owns the WI-0106 day-to-day service, backup/restore, restart and upgrade boundary after the SQLite-to-PostgreSQL cutover.

The final pre-cutover SQLite backup remains a rollback/migration artifact during M24 stabilization. It is not a second writable production catalogue.

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

## Routine logical backup

Use the repository backup wrapper rather than copying the Podman volume or PostgreSQL data directory:

```powershell
.\backup-postgres-catalogue.ps1
```

The script identifies a unique database containing Photo Identity catalogue markers. If multiple migrated/rehearsal databases remain in the server, select the accepted production authority explicitly:

```powershell
.\backup-postgres-catalogue.ps1 -DatabaseName <production-database-name>
```

By default backups are written beneath `%LOCALAPPDATA%\PhotoIdentity\backups\postgresql`. The backup is a `pg_dump` custom-format file copied byte-for-byte from the container, plus a JSON report containing its SHA-256 and source database name. Neither file contains the PostgreSQL password or connection string, but the dump contains the private catalogue and must be protected as sensitive local data.

A normal logical `pg_dump` is transactionally consistent and may be taken while the application is running. For the WI-0106 restore acceptance below, stop the application first and take a fresh backup so exact row counts can be compared deterministically with the restored copy.

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
7. writes an identity-safe restore-verification JSON report.

The isolated verification database is intentionally retained after success or failure. This keeps restore evidence available for maintainer inspection instead of deleting it automatically.

After the report has been reviewed and its verification database name has been copied, remove **only that isolated verification database**, never the production database:

```powershell
Push-Location .\deploy\postgres
$container = (podman compose ps -q postgres).Trim()
$verifyDb = "<exact-verification-database-name-from-report>"
podman exec -e "VERIFY_DATABASE=$verifyDb" $container sh -lc 'dropdb -U "$POSTGRES_USER" --if-exists "$VERIFY_DATABASE"'
Pop-Location
```

Keep the verified backup and its reports through the M24 operational stabilization window. Do not retire the preserved pre-cutover SQLite rollback snapshot until WI-0106 acceptance explicitly records that decision.

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

## Sustained archive catch-up acceptance

Backup/restore and restart acceptance are only the first half of WI-0106. After they pass, resume the real archive through **Advance archive** and let it operate long enough to expose degradation rather than only completing a short smoke test.

Use these privacy-safe operational endpoints while the run is active:

```powershell
$Api = "http://127.0.0.1:5080"
Invoke-RestMethod "$Api/health"
Invoke-RestMethod "$Api/api/archive/status"
Invoke-RestMethod "$Api/api/archive/storage"
Invoke-RestMethod "$Api/api/archive/diagnostics/throughput"
```

The acceptance question is operational stability, not comparison with SQLite. Watch that backlog/progress continues to move, managed hydration is released normally, PostgreSQL health remains ready, and diagnostics expose enough aggregate stage/counter evidence to investigate a stall without enabling per-photo tracing.

After sustained catch-up is accepted, add a small real source increment and run the normal synchronization/advance path without full regeneration. Verify that the new photos synchronize, analyze, enrich and appear for review while unchanged existing work is not unnecessarily repeated. Record that as the final daily-style increment evidence for WI-0106.
