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
- [x] Critical table/entity counts and referential/domain invariants pass before application startup on the maintainer catalogue.
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
- [x] Execute the stopped backup/rehearsal workflow against the maintainer's real catalogue and retain the accepted backup unchanged/read-only.

### 2. Complete schema-driven import
- [x] Discover SQLite and PostgreSQL tables/columns at migration time instead of maintaining a hand-written table list.
- [x] Fail if a populated SQLite table has no PostgreSQL destination table.
- [x] Fail if populated SQLite columns would be discarded or required PostgreSQL columns cannot be supplied/defaulted.
- [x] Order table imports from the PostgreSQL foreign-key graph and preserve explicit GUID/integer identifiers.
- [x] Handle merged-person self references without requiring GUID insertion order.
- [x] Run the importer against the full maintainer SQLite catalogue and resolve any intentionally renamed/retired compatibility state explicitly rather than weakening the no-silent-loss checks.

### 3. Validation and generated-key continuation
- [x] Compare source/copied/target row counts for every shared table before committing the import transaction.
- [x] Verify PostgreSQL foreign-key/check constraints remain validated.
- [x] Repair PostgreSQL identity/serial sequences from imported maxima.
- [x] Emit a privacy-safe JSON migration report containing source backup filename/hash/size, schema versions, per-table counts, critical-domain counts and sequence-repair evidence; never include the PostgreSQL connection string.
- [x] Add live PostgreSQL integration coverage preserving stable source/revision/person/review/suggestion IDs and proving a generated embedding ID advances beyond the imported value.
- [x] Prove the importer itself is deterministic/repeatable by importing one preserved test backup into two separate fresh PostgreSQL databases and comparing source hash, schema versions, copied/per-table/critical counts and sequence-repair evidence.
- [x] Include catalogue migration acceptance in the existing `verify-postgres.ps1` runtime filter.
- [x] Maintainer live PostgreSQL verification passed on 2026-09-10 with the two-target migration acceptance and generated-key continuation enabled.
- [x] Make the real-catalogue rehearsal perform the same-backup/two-target rule automatically: create one immutable backup, import it into two separately created fresh PostgreSQL databases and compare stable migration-report evidence before reporting success.
- [x] Execute that repeatability proof with the maintainer's preserved real-catalogue backup and retain both accepted reports.

### 4. Real-catalogue rehearsal and representative verification
- [x] Add a single rehearsal wrapper that creates fresh PostgreSQL databases from the private local compose configuration and runs backup/import without printing credentials.
- [x] Verify the preserved backup SHA-256 is unchanged after both imports and mark the rehearsal backup read-only before migration begins.
- [x] Automatically reject a repeatability run when source hash/schema/count/sequence evidence differs between the two PostgreSQL imports.
- [x] Allow `-LaunchForReview` to start an isolated PostgreSQL-selected runtime against the primary rehearsal target using a temporary no-secret launcher configuration, and require `/health` to report `catalogueProvider: postgresql`.
- [x] Make source-checkout review independent of an installed Windows package by publishing the current API into an isolated temporary review directory and passing that path explicitly to the launcher.
- [x] Add `review-postgres-rehearsal.ps1` so a successful existing rehearsal database can be reopened for UI acceptance without repeating the full backup/two-target import.
- [x] Add Windows CI coverage that parses both rehearsal PowerShell files, verifies mobile-certificate state is stripped from the local review configuration and verifies the current-API publish/path-override contract.
- [ ] Verify people/face review history and undo/rejection state from the migrated maintainer catalogue.
- [ ] Verify tags, Places and automatic place-enrichment state.
- [ ] Verify saved Smart Collections and slideshow snapshot membership against representative collections.
- [ ] Verify archive coverage, source observations/availability/hydration ownership, processing runs/jobs and completion state.
- [ ] Verify metadata/photo details and person presentation/favorites/visibility state.

### 5. Controlled cutover and rollback
- [x] Document the single-authority backup/import/provider-switch/rollback sequence in `docs/operations/postgresql-catalogue-cutover.md`.
- [x] Add a supported packaged/launcher configuration path that persists `PhotoIdentity__CatalogueProvider=postgresql` while storing only the PostgreSQL connection environment-variable name in `launcher.json`; direct connection strings in launcher settings are rejected.
- [x] Add launcher preflight (`-ValidateConfigurationOnly`), provider validation, secret non-disclosure checks and refusal to switch providers while a healthy process is already running with another authoritative provider.
- [x] Update root/package launcher examples to show the safe environment-variable reference while leaving SQLite as the default provider until cutover.
- [ ] Before cutover, stop the SQLite-authoritative application and retain the source backup unchanged/read-only.
- [ ] Start PostgreSQL-authoritative runtime and verify `/health`, Review, Library/Smart Collections, Archive and background workers before allowing new writes.
- [ ] Record the cutover timestamp and PostgreSQL migration report as acceptance evidence.
- [x] Define rollback as stopping PostgreSQL-authoritative runtime and restoring a working copy of the pre-cutover SQLite backup/configuration; never copy post-cutover PostgreSQL writes back into the preserved backup.
- [ ] Perform maintainer cutover/rollback acceptance before marking WI-0102 complete.

## Migration-tool slice (2026-09-10)

`catalogue migrate --sqlite-backup PATH --postgres-connection-env NAME [--report PATH]` is the offline import boundary. The command does not open the ordinary writable SQLite provider and does not switch application authority. It fingerprints the supplied backup, validates its current SQLite schema and foreign keys, requires a fresh target database, initializes PostgreSQL, then performs the data copy in one target transaction.

The copy plan is generated from both schemas. This is intentional: WI-0102 should fail loudly when a future or previously overlooked SQLite table/column contains authoritative state without a PostgreSQL destination. Shared tables are inserted in PostgreSQL foreign-key dependency order. Stable identifiers are inserted explicitly; PostgreSQL identity/serial sequences are repaired after import. Per-table source/target counts are compared before commit, and the migration report contains aggregate/identifier-safe evidence only.

The live disposable-database integration test seeds immutable catalogue identity, a face/crop/embedding, a person label, review assignment and identity suggestion with explicit integer IDs. It imports that exact backup into two independent fresh PostgreSQL databases, compares the stable report evidence, verifies the imported IDs/relationships in both targets and inserts a new embedding in each target without an explicit ID to prove generated-key continuation. The test is included in the same live PostgreSQL runtime filter used by `verify-postgres.ps1` and was accepted by the maintainer on 2026-09-10.

## Development-complete rehearsal/cutover tooling (2026-09-10)

`catalogue backup` and `rehearse-postgres-migration.ps1` cover the development-side migration workflow. The rehearsal resolves the maintained SQLite catalogue, requires the application to be stopped, creates one consistent SQLite backup through the backup API, marks that backup read-only, creates two independent fresh PostgreSQL rehearsal databases, imports the exact same backup into both and compares stable report evidence. The backup hash is checked again after both imports. Failed/incomplete targets are removed; successful rehearsal targets remain available for inspection.

With `-LaunchForReview`, the script generates a temporary launcher configuration that preserves normal local settings, explicitly selects PostgreSQL, disables inherited mobile-access/certificate state and references a temporary connection-string environment variable. It publishes `PhotoIdentity.Api` from the current checkout into an isolated timestamped directory and passes that directory to the launcher through `-PublishPathOverride`, so source-checkout rehearsal does not assume `%LOCALAPPDATA%\PhotoIdentity\app` already exists. It does not edit the user's normal launcher configuration. The launched runtime must become healthy with `catalogueProvider: postgresql`.

A successful rehearsal target can also be reviewed later without repeating the full migration by running `review-postgres-rehearsal.ps1 -DatabaseName <primary-rehearsal-database>`. That helper rebuilds/publishes the current API, creates a temporary local-only PostgreSQL launcher configuration, resolves the connection string from the private PostgreSQL `.env`, and validates runtime health without modifying production configuration or the rehearsal database.

The normal Windows launcher is ready for final cutover. `launcher.json` may persist `PhotoIdentity__CatalogueProvider=postgresql` plus `postgresConnectionEnvironmentVariable`, but it cannot contain `PhotoIdentity__Postgres__ConnectionString` directly. The launcher resolves the secret from Process/User/Machine environment scope, injects it only into the child process, validates provider agreement with `/health`, and refuses an apparent provider switch while another healthy authority is still running. CI launcher verification exercises the invalid-provider, missing-secret, direct-secret-rejection, preflight and stop-before-switch paths.

## Maintainer rehearsal evidence (2026-09-10)

The real SQLite catalogue produced a validated schema-16 backup of 313,171,968 bytes with SHA-256 `7b39f6d59944e5a3c8b6720bd8cfc922b15b6ec47325883b4be232f153eb469c`. An initial guarded import exposed 27 `identity_match_regeneration_runs` rows whose PostgreSQL tables had previously been created lazily at runtime. That state was treated as authoritative, promoted into fresh PostgreSQL schema initialization and covered by a live migration regression test; the no-silent-loss guard was not weakened.

After that correction, two independent fresh PostgreSQL targets both imported 57 tables and 480,147 rows from the exact same preserved backup, repaired 11 generated sequences and reported `validation: passed`. Their stable reports matched and the rehearsal reported `repeatability: passed` with `production-authority-changed: false`. A second full rehearsal reproduced those same catalogue-level results. The remaining launch-only failures were unrelated to migrated data: first an inherited mobile-certificate password requirement, then absence of a preinstalled `%LOCALAPPDATA%\PhotoIdentity\app` package. Both review-launch assumptions are now removed by the local-only temporary configuration and current-source publish path described above.

At this point the unchecked items intentionally require human review of the migrated runtime or the real authority-transfer window: perform representative UI/domain checks, then execute and accept the PostgreSQL cutover/rollback boundary.
