# PostgreSQL catalogue migration and cutover

WI-0102 moves one existing authoritative SQLite catalogue to PostgreSQL. The migration is an offline authority-transfer operation, not a dual-write deployment. At every point there must be exactly one writable authoritative catalogue.

## Safety rules

- Do not snapshot or migrate the SQLite catalogue while Photo Identity or another process can write it.
- Do not point the normal application configuration at PostgreSQL until the import report and representative verification pass.
- Keep the accepted pre-cutover SQLite backup unchanged until PostgreSQL cutover has been accepted.
- Never copy post-cutover PostgreSQL state back into that preserved backup. A rollback intentionally returns to the pre-cutover state.
- Keep PostgreSQL credentials outside source control and command output. Migration/rehearsal code reads them only from private environment/configuration state.

## 1. Automated real-catalogue rehearsal

The supported rehearsal path is `rehearse-postgres-migration.ps1`. Exit Photo Identity completely first. The script requires the explicit `-ApplicationStopped` acknowledgement and also rejects a detected `PhotoIdentity.Api`/known `dotnet PhotoIdentity.Api` process.

From the repository root:

~~~powershell
.\rehearse-postgres-migration.ps1 -ApplicationStopped -LaunchForReview
~~~

By default the script resolves the SQLite catalogue from the same launcher locations used by Photo Identity, falling back to `%LOCALAPPDATA%\PhotoIdentity\catalogue.db`. If this installation uses another catalogue, specify it explicitly:

~~~powershell
.\rehearse-postgres-migration.ps1 `
  -ApplicationStopped `
  -DatabasePath "C:\path\to\catalogue.db" `
  -LaunchForReview
~~~

The script uses `deploy/postgres/.env`, starts the existing Podman PostgreSQL service if required, and creates a uniquely named fresh database. It never reuses the ordinary verification database or an earlier rehearsal target.

The rehearsal performs these steps in order:

1. builds the Release CLI;
2. creates a timestamped SQLite backup through `catalogue backup` rather than a raw file copy;
3. validates the source/backup schema, SQLite integrity and foreign keys;
4. records the backup SHA-256 and verifies the source file did not change while the backup was created;
5. creates a fresh PostgreSQL rehearsal database;
6. runs `catalogue migrate` and writes a timestamped migration report;
7. verifies the preserved backup hash is still unchanged and marks the backup read-only;
8. leaves the new PostgreSQL target in place for representative review; and
9. with `-LaunchForReview`, starts Photo Identity against that rehearsal database using temporary process environment only, then requires `/health` to report `catalogueProvider: postgresql`.

The script prints the rehearsal database name, backup path/hash and report path but never prints the PostgreSQL password or connection string. `production-authority-changed: false` is expected: the normal launcher configuration remains untouched.

If the rehearsal fails after creating a PostgreSQL target but before a successful import, the script removes that incomplete target. The preserved SQLite backup is never reported as accepted unless its validation succeeds.

## 2. Stopped SQLite backup contract

The underlying backup command can also be used independently:

~~~powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = "<backup-directory>\catalogue-$stamp.db"

dotnet run --project .\src\PhotoIdentity.Cli --configuration Release -- `
  catalogue backup `
  --database "<path-to-active-catalogue.db>" `
  --output $backup `
  --application-stopped
~~~

`--application-stopped` is deliberately mandatory. The command opens the source read-only, requires the current SQLite schema, runs `PRAGMA foreign_key_check`, creates the destination through SQLite's backup API, then reopens the result read-only and runs `PRAGMA integrity_check` plus foreign-key validation. Existing backup paths are never overwritten.

This is the supported replacement for copying only the `.db` file. It creates a logical SQLite snapshot and therefore does not depend on whether the source previously used rollback-journal or WAL mode.

## 3. PostgreSQL import contract

The underlying migration command is:

~~~powershell
$env:PHOTOIDENTITY_MIGRATION_CONNECTION = "<private connection string to a fresh database>"

dotnet run --project .\src\PhotoIdentity.Cli --configuration Release -- `
  catalogue migrate `
  --sqlite-backup $backup `
  --postgres-connection-env PHOTOIDENTITY_MIGRATION_CONNECTION `
  --report ".\artifacts\catalogue-migration-$stamp.json"
~~~

The command:

1. opens the SQLite backup read-only and enables `PRAGMA query_only`;
2. requires the current SQLite schema version and a clean `PRAGMA foreign_key_check`;
3. requires a PostgreSQL database with no existing public tables;
4. initializes the current PostgreSQL schema;
5. compares SQLite and PostgreSQL schemas and refuses to discard populated source state;
6. inserts shared tables in PostgreSQL foreign-key dependency order while preserving explicit stable identifiers;
7. repairs PostgreSQL generated integer sequences;
8. compares source/copied/target row counts and checks PostgreSQL constraint validation; and
9. commits only after the complete import validates.

A successful report contains the backup filename, SHA-256, size, schema versions, per-table row counts, critical-domain counts and sequence-repair count. It intentionally contains no database password or connection string.

A failed migration target is disposable. Diagnose the reported schema/state mismatch, create another fresh PostgreSQL database, and rerun from the same preserved SQLite backup. Do not weaken the no-silent-loss checks merely to make a previously unknown SQLite table disappear.

## 4. Repeatability

The automated live acceptance test imports the same fixture backup into two separate fresh PostgreSQL databases. Both successful reports must agree on source hash/size, schema versions, total rows copied, per-table counts, critical-domain counts and generated-sequence repair coverage. The maintainer's real backup must pass the same rule before final cutover; the second real import should use the exact same read-only backup file.

## 5. Representative verification before authority transfer

`-LaunchForReview` starts an isolated PostgreSQL-selected runtime against the migrated rehearsal target without modifying the normal launcher configuration. Before accepting the migrated state, verify representative examples of:

- people, confirmed/unknown/rejected review state, review history and undo relationships;
- identity suggestions and identity-regeneration policy/state;
- manual tags and first-class Places, including automatic place-enrichment cache/state;
- saved Smart Collections and representative slideshow snapshot membership;
- archive root/included folders, source observations, availability, hydration ownership and storage accounting;
- processing runs/jobs, completed analysis state and derivative/proxy completion;
- capture/extended metadata and Photo Details; and
- person favorites, visibility and featured-face presentation state.

The migration report proves structural completeness and counts; this representative pass proves user-visible meaning. Keep ordinary editing to a minimum during rehearsal because this database is disposable and is not yet the accepted authority.

When review is finished, stop the rehearsal Photo Identity process before restarting the normal SQLite-authoritative application.

## 6. Persisted Windows launcher cutover

The supported launcher now persists the **provider selection** in `launcher.json` but deliberately keeps the PostgreSQL connection string out of that file. The private JSON contains only the name of an environment variable holding the secret.

First put the accepted target connection string in a Windows environment variable. If the final migration already placed it in a process variable, persist that value without retyping it:

~~~powershell
[Environment]::SetEnvironmentVariable(
  "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING",
  $env:PHOTOIDENTITY_MIGRATION_CONNECTION,
  "User")
~~~

Then update the private launcher configuration. Keep all existing non-secret settings and add/change only this provider boundary:

~~~json
{
  "postgresConnectionEnvironmentVariable": "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING",
  "settings": {
    "PhotoIdentity__CatalogueProvider": "postgresql"
  }
}
~~~

The abbreviated example above is not a complete replacement configuration; retain the existing database/archive/proxy/GeoNames settings alongside `PhotoIdentity__CatalogueProvider`. A direct `PhotoIdentity__Postgres__ConnectionString` value under `settings` is intentionally rejected.

The launcher searches Process, User and Machine environment scopes for `postgresConnectionEnvironmentVariable`, copies the resolved value only into the child Photo Identity process, and never prints it. Before starting the app, preflight the exact private configuration:

~~~powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
  -File .\Start-PhotoIdentity.ps1 `
  -ConfigurationPath "$env:LOCALAPPDATA\PhotoIdentity\launcher.json" `
  -ValidateConfigurationOnly
~~~

The preflight must report `catalogueProvider: postgresql` and the environment-variable **name**, never the connection string. It starts no server.

Only after final backup/import/repeatability/representative checks pass should the real switch happen. Stop the SQLite-authoritative Photo Identity process first, then start normally through `PhotoIdentity.cmd` or `Start-PhotoIdentity.ps1`. If a healthy process is already running with a different `catalogueProvider`, the launcher refuses to claim the switch and requires that process to be stopped first.

After start, the launcher itself requires the healthy runtime provider to match the configured provider. Confirm `/health` reports `catalogueProvider: postgresql`, the current PostgreSQL schema version and PostgreSQL status `ready`. Verify Review, Library/Smart Collections, Archive status and background workers before normal writes resume. Record the cutover timestamp and accepted migration report.

Do not delete the pre-cutover SQLite backup. Keep it unchanged through maintainer acceptance and the operational stabilization period owned by later M24 work.

## 7. Rollback boundary

Rollback is intentionally a return to the exact pre-cutover SQLite state, not a reverse migration.

1. Stop the PostgreSQL-authoritative Photo Identity process so no further PostgreSQL writes occur.
2. Preserve PostgreSQL for diagnosis; do not attempt to merge its post-cutover writes into SQLite.
3. Make a new working copy from the unchanged pre-cutover SQLite backup. Do not use or modify the preserved backup itself as the working database.
4. Change `PhotoIdentity__CatalogueProvider` back to `sqlite` and restore the normal SQLite catalogue path. `postgresConnectionEnvironmentVariable` may be removed from the JSON; it is ignored while SQLite is selected.
5. Start Photo Identity and confirm the launcher and `/health` report `catalogueProvider: sqlite`.
6. Verify representative Review, Library and Archive state is the expected pre-cutover state.

Any user changes made after PostgreSQL cutover are outside this rollback snapshot. That limitation is why cutover acceptance should happen before normal writes resume and why the rollback window should be short and controlled.

## 8. Acceptance evidence

WI-0102 is complete only after the maintainer has recorded:

- the final stopped/quiesced SQLite backup filename and SHA-256;
- successful repeatable import reports from that same backup;
- representative domain verification;
- successful PostgreSQL-selected `/health` and runtime checks;
- the cutover timestamp; and
- a rollback rehearsal or accepted rollback verification showing the preserved backup remains unchanged.

Operational backup/recovery of the accepted PostgreSQL catalogue after cutover belongs to WI-0106; this document covers only the WI-0102 migration and immediate authority-transfer boundary.
