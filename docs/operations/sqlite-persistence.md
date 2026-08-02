# SQLite persistence operations

The local SQLite catalogue contains canonical source/revision identity, people, human review history, processing state and model-derived metadata. Treat it as sensitive application data, not a disposable cache.

## Supported deployment shape

- One Photo Identity Indexer installation owns one local catalogue.
- Keep the database on a local disk.
- Do not place it on a network share or in a synchronised cloud folder.
- Multiple local application processes can open connections, but SQLite permits only one writer transaction at a time.
- Repository transactions remain short and never span image decoding, inference, file copying or user interaction.
- Personal OneDrive photos are accessed through the Windows sync client; OneDrive does not host the catalogue.

`SqliteCatalogueDatabase` opens governed connections, enables foreign keys and applies supported schema initialization/migrations. Callers use repository/application services rather than editing rows manually.

## Backup policy

The supported backup is a quiesced maintenance-window copy.

1. Stop the CLI, API/browser host and every worker that can access the catalogue.
2. Confirm no process is writing and no migration or import is running.
3. Copy the database file to a versioned encrypted backup location.
4. Copy referenced crop and governed artefact directories in the same maintenance window.
5. Validate the database copy with:

   ```sql
   PRAGMA integrity_check;
   PRAGMA foreign_key_check;
   PRAGMA user_version;
   ```

6. Accept the backup only when `integrity_check` returns `ok`, `foreign_key_check` returns no rows and the schema version is supported by the recorded application version.

Do not copy an actively written database. A live file copy can capture an inconsistent point relative to journal or WAL sidecars.

When WAL/SHM sidecars exist, stop all writers before copying and preserve the database consistently according to the maintenance procedure. The multi-model comparison workflow creates a private stopped-state backup and retains optional sidecars before processing.

Backups contain identity and biometric data. Encrypt them, restrict access and never commit them or place them in public storage.

## Restore procedure

1. Stop every process that can access the catalogue.
2. Preserve the current database and associated diagnostic files rather than overwriting them immediately.
3. Restore the selected database and matching artefacts from the same maintenance window.
4. Run `integrity_check`, `foreign_key_check` and `user_version` against the restored file.
5. Start the current supported application version so initialization can verify the schema.
6. Confirm representative sources, revisions, people, review state and processing runs are readable.
7. Confirm crop/artefact references resolve before deleting the preserved diagnostic copy.

The application rejects a catalogue whose schema version is newer than it supports. Downgrading an application against a newer catalogue is unsupported.

## Concurrent writers and locking

Repository methods own transaction boundaries. Do not keep a connection or transaction open during non-database work.

Important atomic operations include:

- source, asset and immutable revision changes;
- complete face-inspection graph persistence;
- person and review actions;
- processing-run and job creation;
- job claiming and attempt updates; and
- governed bundle-result import.

The persistence layer does not hide sustained `SQLITE_BUSY` or `SQLITE_LOCKED` failures. Orchestration should use a bounded retry policy where appropriate and surface a clear failure when the retry budget is exhausted. Do not use unbounded retries.

Operational recovery:

1. stop unnecessary writers;
2. allow any active short transaction to finish;
3. inspect the persisted run with `batch status`;
4. use `batch resume` with the original run ID; and
5. investigate repeated failures rather than editing job state manually.

Accepted restart/resume verification proves that saved run configuration and model selection survive interruption. Resume must not silently change source scope or selected models.

## Resumable processing state

A processing run persists the selected detector and embedder IDs plus its jobs and attempts. Closing a terminal does not mean the run should be replaced.

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch status --database $db --run $runId

dotnet run --project src/PhotoIdentity.Cli -- `
  batch resume --database $db --run $runId
```

Use bounded attempt settings when intentionally testing failure recovery. Repeatedly starting replacement runs can duplicate operational work even when canonical revision writes remain idempotent.

## Artefact consistency

The database references crop and other derived artefact locations. A database-only backup preserves canonical identities and review history but can leave derived references unresolved.

Back up the catalogue and required artefact directories together, or retain enough immutable source and exact model provenance to regenerate replaceable artefacts.

Missing original photos are not repaired by a catalogue restore. Original sources remain separate read-only inputs.

## Schema upgrade policy

Released schema versions are immutable. Future changes use forward-only migrations.

For each migration:

1. require or strongly prompt for a verified backup of representative user data;
2. accept exactly the supported previous version;
3. execute schema and data changes transactionally;
4. validate transformed rows before removing or renaming old structures;
5. record migration history and update `PRAGMA user_version` in the same transaction;
6. test both fresh initialization and upgrade from the previous released version; and
7. retain startup rejection for catalogues newer than the application.

For SQLite table-shape changes, create the replacement table, copy and validate data, recreate indexes/foreign keys and swap structures inside the migration transaction.

Down migrations are not supported. Restore the pre-upgrade backup when rollback is required.

## Bundle imports

Portable result imports validate checksums, schema, immutable revision IDs and exact model provenance before writing derived state. Imports preserve people and human review history and are not a substitute for catalogue restore.

Keep bundle import transactions bounded. Do not perform remote transfer or model inference inside the database transaction.

## Operational verification

Run after persistence or run-recovery changes:

```powershell
dotnet test `
  tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj

dotnet run --project tools/PhotoIdentity.Docs -- validate

dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Before releasing a migration or recovery change, also exercise backup, upgrade/resume and restore against a disposable copy of a representative catalogue.

See the [Local operator guide](local-operator-guide.md), [Canonical data model](../architecture/data-model.md) and [Glossary](../glossary.md).
