# PostgreSQL catalogue migration and cutover

WI-0102 moves one existing authoritative SQLite catalogue to PostgreSQL. The migration is an offline authority-transfer operation, not a dual-write deployment. At every point there must be exactly one writable authoritative catalogue.

## Safety rules

- Do not migrate from the SQLite file while Photo Identity or another process can write it.
- Do not point the application at PostgreSQL until the import report and representative verification pass.
- Keep the accepted pre-cutover SQLite backup unchanged until PostgreSQL cutover has been accepted.
- Never copy post-cutover PostgreSQL state back into that preserved backup. A rollback intentionally returns to the pre-cutover state.
- Keep PostgreSQL credentials outside source control and command output. The migration command accepts the connection string only through a named environment variable.

## 1. Rehearsal backup

A rehearsal must use a copy of the real catalogue, not the active catalogue itself. For the final migration, first exit Photo Identity completely so hosted workers and API requests cannot write SQLite. Confirm no Photo Identity process remains before copying the database.

Choose a timestamped backup name and copy the stopped catalogue:

~~~powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$source = "<path-to-active-catalogue.db>"
$backup = "<backup-directory>\catalogue-$stamp.db"
Copy-Item -LiteralPath $source -Destination $backup
Get-FileHash -LiteralPath $backup -Algorithm SHA256
~~~

Do not resume the SQLite-authoritative application during the final migration/cutover window. The migration command computes the same SHA-256 and records it in its report, so the operator hash can be matched to the imported backup.

If the active SQLite database uses WAL mode, the supported final procedure is still to stop the application first and then copy the database file. Do not manually assemble a live `.db`/`-wal`/`-shm` snapshot while writers are running.

## 2. Prepare a fresh PostgreSQL target

The migration command deliberately rejects a PostgreSQL database that already contains public tables. Rehearsal and final migration therefore use a newly created empty database. Do not reuse the WI-0101 verification catalogue or a prior failed/rehearsal target.

Use the local PostgreSQL runtime described in `postgresql-local-runtime.md`, create the target database, and put its private connection string in a process environment variable, for example:

~~~powershell
$env:PHOTOIDENTITY_MIGRATION_CONNECTION = "Host=127.0.0.1;Port=5432;Database=<fresh-database>;Username=<user>;Password=<private-password>;SSL Mode=Disable;GSS Encryption Mode=Disable;Pooling=false"
~~~

The environment-variable name may differ; the value must not be committed or pasted into the migration report.

## 3. Import the preserved SQLite backup

From the repository root:

~~~powershell
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

## 4. Repeatability rehearsal

Before final cutover, import the same backup into two separate fresh PostgreSQL databases. Both successful reports must agree on:

- source SHA-256 and byte length;
- SQLite and PostgreSQL schema versions;
- total rows copied;
- per-table source/target counts;
- critical-domain counts; and
- generated-sequence repair coverage.

Timestamps and target database identity are not equivalence inputs. The automated live migration test exercises this same-backup/two-target rule on a representative catalogue fixture.

## 5. Representative verification before authority transfer

Keep the application stopped or, for a rehearsal only, run an isolated PostgreSQL-selected instance against the imported target. Verify representative state before accepting the target:

- people, confirmed/unknown/rejected review state, review history and undo relationships;
- identity suggestions and identity-regeneration policy/state;
- manual tags and first-class Places, including automatic place enrichment cache/state;
- saved Smart Collections and representative slideshow snapshot membership;
- archive root/included folders, source observations, availability, hydration ownership and storage accounting;
- processing runs/jobs, completed analysis state and derivative/proxy completion;
- capture/extended metadata and Photo Details; and
- person favorites, visibility and featured-face presentation state.

The migration report proves structural completeness and counts; this representative pass proves user-visible meaning.

## 6. Cut over exactly one writable authority

Only after the final backup/import/verification passes should the runtime provider be changed. Keep the SQLite-authoritative application stopped. Supply the PostgreSQL connection string and provider selection to the process:

~~~powershell
$env:PhotoIdentity__CatalogueProvider = "postgresql"
$env:PhotoIdentity__Postgres__ConnectionString = $env:PHOTOIDENTITY_MIGRATION_CONNECTION
~~~

Then start Photo Identity normally. PostgreSQL-selected startup must not open or migrate the SQLite catalogue. Confirm `/health` reports:

- `catalogueProvider: postgresql`;
- the current PostgreSQL schema version; and
- PostgreSQL status `ready`.

Before normal use, verify Review, Library/Smart Collections, Archive status and the relevant hosted workers. Record the cutover time and the accepted migration report. Only then allow ordinary new writes in PostgreSQL-authoritative mode.

Do not delete the pre-cutover SQLite backup. Keep it read-only/unchanged through maintainer acceptance and the operational stabilization period owned by later M24 work.

## 7. Rollback boundary

Rollback is intentionally a return to the exact pre-cutover SQLite state, not a reverse migration.

1. Stop the PostgreSQL-authoritative Photo Identity process first so no further PostgreSQL writes can occur.
2. Preserve PostgreSQL for diagnosis; do not attempt to merge its post-cutover writes into SQLite.
3. Make a new working copy from the unchanged pre-cutover SQLite backup. Do not use or modify the preserved backup itself as the working database.
4. Remove/set aside `PhotoIdentity__CatalogueProvider=postgresql` and restore the normal SQLite catalogue path/configuration.
5. Start Photo Identity and confirm `/health` reports `catalogueProvider: sqlite`.
6. Verify representative Review, Library and Archive state is the expected pre-cutover state.

Any user changes made after PostgreSQL cutover are outside this rollback snapshot. That limitation is why cutover acceptance should happen before normal writes resume and why the rollback window should be short and controlled.

## 8. Acceptance evidence

WI-0102 is complete only after the maintainer has recorded:

- the final stopped/quiesced SQLite backup filename and SHA-256;
- successful repeatable import reports;
- representative domain verification;
- successful PostgreSQL-selected `/health` and runtime checks;
- the cutover timestamp; and
- a rollback rehearsal or accepted rollback verification showing the preserved backup remains unchanged.

Operational backup/recovery of the accepted PostgreSQL catalogue after cutover belongs to WI-0106; this document covers only the WI-0102 migration and immediate authority-transfer boundary.
