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
- [ ] Migration is repeatable from the same SQLite backup into an empty PostgreSQL database.
- [ ] Critical table/entity counts and referential/domain invariants pass before application startup.
- [ ] Review history, identities, tags/Places, smart collections and processing state survive representative verification.
- [ ] New inserts after migration do not collide with imported integer sequences.
- [ ] Cutover selects exactly one authoritative writable database.
- [ ] Rollback can restore the pre-cutover SQLite application state without modifying the preserved backup.

## Implementation checklist

### 1. Offline source and target safety
- [x] Add an explicit `catalogue migrate` CLI path that requires `--sqlite-backup` and a PostgreSQL connection supplied through a named environment variable.
- [x] Open the SQLite input read-only and set `PRAGMA query_only = ON`; reject backups not at the current SQLite schema version or failing `PRAGMA foreign_key_check`.
- [x] Require a PostgreSQL target with no existing public tables before initializing the current PostgreSQL schema.
- [ ] Add the final operator backup step that produces a timestamped SQLite backup only after the application is stopped/quiesced, and record its SHA-256 before migration.

### 2. Complete schema-driven import
- [x] Discover SQLite and PostgreSQL tables/columns at migration time instead of maintaining a hand-written table list.
- [x] Fail if a populated SQLite table has no PostgreSQL destination table.
- [x] Fail if populated SQLite columns would be discarded or required PostgreSQL columns cannot be supplied/defaulted.
- [x] Order table imports from the PostgreSQL foreign-key graph and preserve explicit GUID/integer identifiers.
- [x] Handle merged-person self references without requiring GUID insertion order.
- [ ] Run the importer against a realistic full SQLite catalogue and resolve any intentionally renamed/retired compatibility state explicitly rather than weakening the no-silent-loss checks.

### 3. Validation and generated-key continuation
- [x] Compare source/copied/target row counts for every shared table before committing the import transaction.
- [x] Verify PostgreSQL foreign-key/check constraints remain validated.
- [x] Repair PostgreSQL identity/serial sequences from imported maxima.
- [x] Emit a privacy-safe JSON migration report containing source backup filename/hash/size, schema versions, per-table counts, critical-domain counts and sequence-repair evidence; never include the PostgreSQL connection string.
- [x] Add live PostgreSQL integration coverage preserving stable source/revision/person/review/suggestion IDs and proving a generated embedding ID advances beyond the imported value.
- [x] Include catalogue migration acceptance in the existing `verify-postgres.ps1` runtime filter.
- [ ] Prove repeatability by importing the same preserved SQLite backup into a second fresh PostgreSQL database and comparing the validation reports/critical counts.

### 4. Representative domain verification
- [ ] Verify people/face review history and undo/rejection state from the migrated maintainer catalogue.
- [ ] Verify tags, Places and automatic place-enrichment state.
- [ ] Verify saved Smart Collections and slideshow snapshot membership against representative collections.
- [ ] Verify archive coverage, source observations/availability/hydration ownership, processing runs/jobs and completion state.
- [ ] Verify metadata/photo details and person presentation/favorites/visibility state.

### 5. Controlled cutover and rollback
- [ ] Document and test the exact launcher/application configuration change from SQLite authority to `PhotoIdentity:CatalogueProvider=postgresql`.
- [ ] Before cutover, stop the SQLite-authoritative application and retain the source backup unchanged/read-only.
- [ ] Start PostgreSQL-authoritative runtime and verify `/health`, Review, Library/Smart Collections, Archive and background workers before allowing new writes.
- [ ] Record the cutover timestamp and PostgreSQL migration report as acceptance evidence.
- [ ] Define rollback as stopping PostgreSQL-authoritative runtime and restoring the pre-cutover SQLite application configuration/catalogue copy; never copy post-cutover PostgreSQL writes back into the preserved backup.
- [ ] Perform maintainer cutover/rollback acceptance before marking WI-0102 complete.

## First migration-tool slice (2026-09-10)

`catalogue migrate --sqlite-backup PATH --postgres-connection-env NAME [--report PATH]` is the offline import boundary. The command does not open the ordinary writable SQLite provider and does not switch application authority. It fingerprints the supplied backup, validates its current SQLite schema and foreign keys, requires a fresh target database, initializes PostgreSQL, then performs the data copy in one target transaction.

The copy plan is generated from both schemas. This is intentional: WI-0102 should fail loudly when a future or previously overlooked SQLite table/column contains authoritative state without a PostgreSQL destination. Shared tables are inserted in PostgreSQL foreign-key dependency order. Stable identifiers are inserted explicitly; PostgreSQL identity/serial sequences are repaired after import. Per-table source/target counts are compared before commit, and the migration report contains aggregate/identifier-safe evidence only.

A live disposable-database integration test seeds immutable catalogue identity, a face/crop/embedding, a person label, review assignment and identity suggestion with explicit integer IDs. It verifies those IDs and relationships after migration and inserts another embedding without an explicit ID to prove sequence continuation. The test is included in the same live PostgreSQL runtime filter used by `verify-postgres.ps1`.

This slice is not production cutover. The remaining acceptance work is to run the importer against the maintainer's preserved full SQLite backup, close any real-schema compatibility gaps, prove repeatability, verify representative domains, then perform the single-authority configuration cutover with a preserved rollback copy.
