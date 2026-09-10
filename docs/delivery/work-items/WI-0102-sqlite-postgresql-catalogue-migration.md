---
id: WI-0102
title: Migrate the existing SQLite catalogue and perform controlled PostgreSQL cutover
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0099, WI-0100, WI-0101]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Cli, PhotoIdentity.Api, operations, tests]
---

# WI-0102: Migrate the existing SQLite catalogue and perform controlled PostgreSQL cutover

## Objective
Provide a repeatable, verifiable migration of the maintainer's existing catalogue to PostgreSQL with a safe rollback boundary.

WI-0101 completed the PostgreSQL runtime/persistence boundary in PR #277. WI-0102 is the controlled data-migration and authority-transfer item: it must preserve the existing catalogue, prove the imported PostgreSQL state, then switch exactly one writable authority. No dual-write bridge is permitted.

## In scope
- Build a migration/import command that reads SQLite and writes a fresh compatible PostgreSQL catalogue.
- Preserve stable GUID/integer identities, timestamps, hashes, review/identity history, processing state and relationships.
- Define ordering for identity/sequence values and validate generated-key continuation after import.
- Validate row counts plus critical domain invariants and PostgreSQL foreign keys before cutover.
- Require the source SQLite catalogue to be quiesced/read-only during final migration.
- Keep a timestamped backup/read-only SQLite copy until maintainer acceptance.
- Provide explicit cutover and rollback instructions; never run two writable authoritative catalogues.

## Acceptance criteria
- [x] Migration is repeatable from the same SQLite backup into an empty PostgreSQL database.
- [ ] Critical table/entity counts and referential/domain invariants pass before application startup on the maintainer catalogue.
- [ ] Review history, identities, tags/Places, smart collections and processing state survive representative maintainer-catalogue verification.
- [x] New inserts after migration do not collide with imported integer sequences.
- [ ] Cutover selects exactly one authoritative writable database.
- [ ] Rollback can restore the pre-cutover SQLite application state without modifying the preserved backup.

## Implementation checklist

### 1. Offline source and target safety
- [x] Add an explicit `catalogue migrate` CLI path that requires `--sqlite-backup` and a PostgreSQL connection supplied through a named environment variable.
- [x] Open the SQLite input read-only and set `PRAGMA query_only = ON`; reject backups not at the current SQLite schema version or failing `PRAGMA foreign_key_check`.
- [x] Require a PostgreSQL target with no existing public tables before initializing the current PostgreSQL schema.
- [x] Add `catalogue backup --database PATH --output PATH --application-stopped`; create the snapshot through SQLite's backup API, verify current schema/integrity/FKs, fingerprint the source before/after and refuse backup overwrite.
- [x] Add `rehearse-postgres-migration.ps1` to resolve the launcher/default catalogue, require stopped-app acknowledgement, reject a detected Photo Identity process, create a timestamped backup and keep production authority unchanged.
- [ ] Execute the stopped backup/rehearsal workflow against the maintainer's real catalogue and retain the accepted backup unchanged/read-only.

### 2. Complete schema-driven import
- [x] Discover SQLite and PostgreSQL tables/columns at migration time instead of maintaining a hand-written table list.
- [x] Fail if a populated SQLite table has no PostgreSQL destination table.
- [x] Fail if populated SQLite columns would be discarded or required PostgreSQL columns cannot be supplied/defaulted.
- [x] Order table imports from the PostgreSQL foreign-key graph and preserve explicit GUID/integer identifiers.
- [x] Handle merged-person self references without requiring GUID insertion order.
- [ ] Run the importer against the full maintainer SQLite catalogue and resolve any intentionally renamed/retired compatibility state explicitly rather than weakening the no-silent-loss checks.

### 3. Validation and generated-key continuation
- [x] Compare source/copied/target row counts for every shared table before committing the import transaction.
- [x] Verify PostgreSQL foreign-key/check constraints remain validated.
- [x] Repair PostgreSQL identity/serial sequences from imported maxima.
- [x] Emit a privacy-safe JSON migration report containing source backup filename/hash/size, schema versions, per-table counts, critical-domain counts and sequence-repair evidence; never include the PostgreSQL connection string.
- [x] Add live PostgreSQL integration coverage preserving stable source/revision/person/review/suggestion IDs and proving a generated embedding ID advances beyond the imported value.
- [x] Prove the importer itself is deterministic/repeatable by importing one preserved test backup into two separate fresh PostgreSQL databases and comparing source hash, schema versions, copied/per-table/critical counts and sequence-repair evidence.
- [x] Include catalogue migration acceptance in the existing `verify-postgres.ps1` runtime filter.
- [x] Maintainer live PostgreSQL verification passed on 2026-09-10 with the two-target migration acceptance and generated-key continuation enabled.
- [ ] Prove repeatability with the maintainer's preserved real-catalogue backup by importing the same file into a second fresh PostgreSQL database and comparing the accepted reports.

### 4. Real-catalogue rehearsal and representative verification
- [x] Add a single rehearsal wrapper that creates a fresh PostgreSQL database from the private local compose configuration and runs backup/import without printing credentials.
- [x] Verify the preserved backup SHA-256 is unchanged after migration and mark the successful rehearsal backup read-only.
- [x] Allow `-LaunchForReview` to start an isolated PostgreSQL-selected runtime against the rehearsal target using temporary process environment only, and require `/health` to report `catalogueProvider: postgresql`.
- [ ] Verify people/face review history and undo/rejection state from the migrated maintainer catalogue.
- [ ] Verify tags, Places and automatic place-enrichment state.
- [ ] Verify saved Smart Collections and slideshow snapshot membership against representative collections.
- [ ] Verify archive coverage, source observations/availability/hydration ownership, processing runs/jobs and completion state.
- [ ] Verify metadata/photo details and person presentation/favorites/visibility state.

### 5. Controlled cutover and rollback
- [x] Document the single-authority backup/import/provider-switch/rollback sequence in `docs/operations/postgresql-catalogue-cutover.md`, including the runtime environment keys `PhotoIdentity__CatalogueProvider=postgresql` and `PhotoIdentity__Postgres__ConnectionString`.
- [ ] Test the exact packaged/launcher configuration path that persists/applies PostgreSQL authority rather than relying only on temporary shell environment variables.
- [ ] Before cutover, stop the SQLite-authoritative application and retain the source backup unchanged/read-only.
- [ ] Start PostgreSQL-authoritative runtime and verify `/health`, Review, Library/Smart Collections, Archive and background workers before allowing new writes.
- [ ] Record the cutover timestamp and PostgreSQL migration report as acceptance evidence.
- [x] Define rollback as stopping PostgreSQL-authoritative runtime and restoring a working copy of the pre-cutover SQLite backup/configuration; never copy post-cutover PostgreSQL writes back into the preserved backup.
- [ ] Perform maintainer cutover/rollback acceptance before marking WI-0102 complete.

## Migration-tool slice (2026-09-10)

`catalogue migrate --sqlite-backup PATH --postgres-connection-env NAME [--report PATH]` is the offline import boundary. The command does not open the ordinary writable SQLite provider and does not switch application authority. It fingerprints the supplied backup, validates its current SQLite schema and foreign keys, requires a fresh target database, initializes PostgreSQL, then performs the data copy in one target transaction.

The copy plan is generated from both schemas. This is intentional: WI-0102 should fail loudly when a future or previously overlooked SQLite table/column contains authoritative state without a PostgreSQL destination. Shared tables are inserted in PostgreSQL foreign-key dependency order. Stable identifiers are inserted explicitly; PostgreSQL identity/serial sequences are repaired after import. Per-table source/target counts are compared before commit, and the migration report contains aggregate/identifier-safe evidence only.

The live disposable-database integration test seeds immutable catalogue identity, a face/crop/embedding, a person label, review assignment and identity suggestion with explicit integer IDs. It imports that exact backup into two independent fresh PostgreSQL databases, compares the stable report evidence, verifies the imported IDs/relationships in both targets and inserts a new embedding in each target without an explicit ID to prove generated-key continuation. The test is included in the same live PostgreSQL runtime filter used by `verify-postgres.ps1` and was accepted by the maintainer on 2026-09-10.

## Real-catalogue rehearsal slice (2026-09-10)

The next slice adds `catalogue backup` and `rehearse-postgres-migration.ps1`. The backup command requires explicit stopped-application acknowledgement, opens the source SQLite catalogue read-only, verifies schema/FKs, creates a logical snapshot through SQLite's backup API and validates the result with `PRAGMA integrity_check` plus foreign-key checks. It fingerprints the source before and after snapshot creation and refuses to overwrite an existing backup.

The rehearsal wrapper resolves the same launcher/default catalogue path used by the application, checks for a still-running Photo Identity process, creates the timestamped backup, starts the private Podman PostgreSQL service, creates a unique fresh rehearsal database, runs the schema-driven migration and verifies that the backup hash remained unchanged. A successful backup is marked read-only. With `-LaunchForReview`, the wrapper starts Photo Identity against the rehearsal target using inherited process environment only, so the normal launcher configuration and SQLite authority remain untouched; `/health` must confirm PostgreSQL selection.

`docs/operations/postgresql-catalogue-cutover.md` now makes this stopped logical-backup/rehearsal wrapper the primary operator path. Raw file copying is no longer the recommended SQLite backup mechanism.

The remaining acceptance work is deliberately real-data and authority-transfer work: run this rehearsal against the maintainer catalogue, close any populated-schema compatibility gaps, verify representative domains, repeat the accepted real backup into a second fresh target, then add/test the persistent packaged-launcher PostgreSQL configuration and perform the controlled cutover/rollback acceptance.
