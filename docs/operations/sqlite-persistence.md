# SQLite persistence operations

This document defines the supported operational policy for the local Photo Identity Indexer catalogue. The database contains durable catalogue state, human identity decisions, model-derived metadata and processing state. Treat it as sensitive application data rather than a disposable cache.

## Supported deployment shape

- The catalogue is a local SQLite database owned by one Photo Identity Indexer installation.
- Multiple application workers may open connections to the same local database file.
- SQLite still permits only one writer transaction at a time. Repository transactions must remain short and must not span image decoding, model inference, file copying or user interaction.
- Network shares, synchronised cloud folders and direct multi-machine access are not supported database locations. Keep the database on a local disk and synchronise source photos through the source adapter instead.

`SqliteCatalogueDatabase` opens non-pooled connections, enables foreign keys on every connection and currently uses SQLite's default rollback-journal behaviour. The adapter does not enable WAL mode or provide an application-level busy retry loop.

## Backup policy

The supported backup method is a quiesced file copy.

1. Stop the CLI, review application and every processing worker that can access the catalogue.
2. Confirm that no Photo Identity Indexer process has the database open and that no migration or processing transaction is running.
3. Copy the database file to a versioned backup location.
4. Open the copy with a SQLite tool and run:

   ```sql
   PRAGMA integrity_check;
   PRAGMA foreign_key_check;
   PRAGMA user_version;
   ```

5. Accept the backup only when `integrity_check` returns `ok`, `foreign_key_check` returns no rows and the schema version is supported by the application version recorded with the backup.

Do not copy the database while writers are active. A plain live-file copy can capture an inconsistent point between database and journal state. Online backup is not currently exposed by the adapter; a future implementation should use SQLite's online backup API or `VACUUM INTO`, with integration coverage, rather than copying an open file.

The database references aligned crop files by path. A database-only backup preserves people, human labels, suggestions, revision history and processing state, but it does not copy those crop files or the original photo archive. Either back up the referenced crop directory in the same maintenance window or retain enough source and model provenance to regenerate it.

Backups contain biometric and identity data. Store them encrypted, restrict access, and never commit them to the repository or place them in public cloud storage.

## Restore procedure

1. Stop every process that can access the catalogue.
2. Preserve the current database file under a diagnostic name rather than overwriting it immediately.
3. Restore the selected backup to the configured catalogue path.
4. Run `PRAGMA integrity_check`, `PRAGMA foreign_key_check` and `PRAGMA user_version` against the restored file.
5. Start the application and call `SqliteCatalogueDatabase.InitializeAsync` before processing work.
6. Confirm that the application can read representative sources, revisions, labels and processing runs before deleting the preserved diagnostic copy.

The application rejects a database whose `user_version` is newer than `CurrentSchemaVersion`. Downgrading the application against a newer catalogue is unsupported.

## Concurrent writers

Repository methods own their transaction boundaries. Callers must not nest them inside long-running work or retain an open connection while performing non-database operations.

- Source, asset and revision writes are atomic.
- A complete face inspection graph is atomic.
- Person-plus-label writes are atomic.
- Run-plus-job creation is atomic.
- Job claiming is atomic and the concurrent-worker integration test verifies that two workers receive distinct due jobs.

The repository layer does not hide sustained `SQLITE_BUSY` or `SQLITE_LOCKED` failures. Orchestration code should treat them as transient, use a bounded retry with jitter, and surface a clear failure after the retry budget is exhausted. Do not use unbounded retries.

Job claims do not expire. If a worker terminates after claiming work, the job remains `running`; WI-0013 must define explicit abandoned-claim recovery before automatic requeueing is introduced. This avoids silently executing the same biometric processing job twice.

## Schema upgrade policy

Schema version 1 is released and must not be edited in place. Future changes use forward-only migrations.

For each new schema version:

1. Add a new migration that accepts exactly the previous supported version.
2. Require a verified backup before applying the migration to a user catalogue.
3. Execute schema and data changes in one transaction.
4. Validate transformed rows before removing or renaming old structures.
5. Insert the new `schema_migrations` row and update `PRAGMA user_version` at the end of the same transaction.
6. Add integration tests for both a fresh database and an upgrade from the previous released version.
7. Keep startup rejection for catalogues newer than the running application.

For table-shape changes that SQLite cannot express directly, create the replacement table, copy and validate data, recreate indexes and foreign keys, then swap tables inside the migration transaction.

Down migrations are not supported. Restore the pre-upgrade backup when rollback is required.

## Operational verification

Run the normal project validation after persistence changes:

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Before releasing a schema migration, also exercise backup, upgrade and restore against a disposable copy of a representative catalogue.