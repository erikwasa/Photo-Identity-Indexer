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
- [x] Review history, identities, tags/Places, smart collections and processing state survive representative maintainer-catalogue verification.
- [x] New inserts after migration do not collide with imported integer sequences.
- [x] Cutover selects exactly one authoritative writable database.
- [x] Rollback can restore the pre-cutover SQLite application state without modifying the preserved backup.

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
- [x] Add Windows CI coverage that parses the rehearsal PowerShell file so syntax regressions fail an integration shard before maintainer use.
- [x] Add `review-postgres-rehearsal.ps1` so a successful rehearsal database can be reopened for UI acceptance without repeating the catalogue backup/import, and publish the current checkout into an isolated review directory rather than requiring a preinstalled package.
- [x] Ensure local rehearsal review disables inherited mobile-certificate settings and surfaces API startup log tails when the published runtime exits before health is reached.
- [x] Fix PostgreSQL identity-regeneration active-run reads so the data reader is disposed before transaction commit; the migrated catalogue exposed the reader-lifetime defect when its preserved active regeneration state caused the hosted service to execute `GetNextActiveAsync` immediately after startup.
- [x] Add live PostgreSQL regression coverage for `GetNextActiveAsync` with an active regeneration run so transaction commit cannot regress while a reader remains open.
- [x] Verify people/face review history and undo/rejection state from the migrated maintainer catalogue.
- [x] Verify tags, Places and automatic place-enrichment state.
- [x] Verify saved Smart Collections and slideshow snapshot membership against representative collections.
- [x] Verify archive coverage, source observations/availability/hydration ownership, processing runs/jobs and completion state.
- [x] Verify metadata/photo details and person presentation/favorites/visibility state.

### 5. Controlled cutover and rollback
- [x] Document the single-authority backup/import/provider-switch/rollback sequence in `docs/operations/postgresql-catalogue-cutover.md`.
- [x] Add a supported packaged/launcher configuration path that persists `PhotoIdentity__CatalogueProvider=postgresql` while storing only the PostgreSQL connection environment-variable name in `launcher.json`; direct connection strings in launcher settings are rejected.
- [x] Add launcher preflight (`-ValidateConfigurationOnly`), provider validation, secret non-disclosure checks and refusal to switch providers while a healthy process is already running with another authoritative provider.
- [x] Update root/package launcher examples to show the safe environment-variable reference while leaving SQLite as the default provider until cutover.
- [x] Before cutover, stop the SQLite-authoritative application and retain the source backup unchanged/read-only.
- [x] Start PostgreSQL-authoritative runtime and verify `/health`, Review, Library/Smart Collections, Archive and background workers before allowing new writes.
- [x] Record the cutover timestamp and PostgreSQL migration report as acceptance evidence.
- [x] Define rollback as stopping PostgreSQL-authoritative runtime and restoring a working copy of the pre-cutover SQLite backup/configuration; never copy post-cutover PostgreSQL writes back into the preserved backup.
- [x] Perform maintainer cutover/rollback acceptance before marking WI-0102 complete.

## Migration-tool slice (2026-09-10)

`catalogue migrate --sqlite-backup PATH --postgres-connection-env NAME [--report PATH]` is the offline import boundary. The command does not open the ordinary writable SQLite provider and does not switch application authority. It fingerprints the supplied backup, validates its current SQLite schema and foreign keys, requires a fresh target database, initializes PostgreSQL, then performs the data copy in one target transaction.

The copy plan is generated from both schemas. This is intentional: WI-0102 should fail loudly when a future or previously overlooked SQLite table/column contains authoritative state without a PostgreSQL destination. Shared tables are inserted in PostgreSQL foreign-key dependency order. Stable identifiers are inserted explicitly; PostgreSQL identity/serial sequences are repaired after import. Per-table source/target counts are compared before commit, and the migration report contains aggregate/identifier-safe evidence only.

The live disposable-database integration test seeds immutable catalogue identity, a face/crop/embedding, a person label, review assignment and identity suggestion with explicit integer IDs. It imports that exact backup into two independent fresh PostgreSQL databases, compares the stable report evidence, verifies the imported IDs/relationships in both targets and inserts a new embedding in each target without an explicit ID to prove generated-key continuation. The test is included in the same live PostgreSQL runtime filter used by `verify-postgres.ps1` and was accepted by the maintainer on 2026-09-10.

## Maintainer real-catalogue rehearsal evidence (2026-09-10)

The real stopped SQLite catalogue was backed up and migrated repeatedly without modifying production authority. The accepted backup characteristics are:

- SQLite schema version 16.
- 313,171,968 bytes.
- SHA-256 `7b39f6d59944e5a3c8b6720bd8cfc922b15b6ec47325883b4be232f153eb469c`.

The first guarded import exposed 27 rows in `identity_match_regeneration_runs` whose PostgreSQL control tables were previously created lazily by the runtime repository. Those tables were promoted into fresh PostgreSQL schema initialization and live migration coverage now proves regeneration run/target state survives the import. The no-silent-loss guard remained enabled.

Subsequent full rehearsals imported the exact same source content independently into two fresh PostgreSQL databases. Each import copied 57 tables / 480,147 rows, repaired 11 generated sequences and returned `validation: passed`; stable migration reports compared equal, producing `repeatability: passed` and `production-authority-changed: false`.

UI review setup then exposed three review-only assumptions without invalidating migration evidence: inherited mobile certificate settings required an unrelated password secret, the launcher assumed an installed `%LOCALAPPDATA%\PhotoIdentity\app`, and the migrated active identity-regeneration state exercised a PostgreSQL `GetNextActiveAsync` reader-lifetime bug. Rehearsal review is now local-only, publishes the current checkout itself, can reuse an already-successful rehearsal target, surfaces startup logs directly, and closes the regeneration data reader before committing its transaction. Production SQLite authority and the accepted backup remained unchanged during this rehearsal phase.

## Development-complete rehearsal/cutover tooling (2026-09-10)

`catalogue backup`, `catalogue migrate`, `rehearse-postgres-migration.ps1`, and `review-postgres-rehearsal.ps1` cover the development-side migration and review workflow. The normal Windows launcher is also ready for final cutover. `launcher.json` may persist `PhotoIdentity__CatalogueProvider=postgresql` plus `postgresConnectionEnvironmentVariable`, but it cannot contain `PhotoIdentity__Postgres__ConnectionString` directly. The launcher resolves the secret from Process/User/Machine environment scope, injects it only into the child process, validates provider agreement with `/health`, and refuses an apparent provider switch while another healthy authority is still running.

## Final maintainer cutover acceptance (2026-09-11)

PR #278 merged to `main` at `811569f06d2db7b973af32c12e0e0b28bb1cb74f` after workflow #1599 passed on the PR head. From merged `main`, the maintainer ran the final stopped-source two-target rehearsal with `-ApplicationStopped -LaunchForReview` and accepted the generated immutable backup, both migration reports and representative UI/domain state.

The persistent Windows launcher was then configured with `PhotoIdentity__CatalogueProvider=postgresql` and an environment-variable reference for the PostgreSQL connection string. The normal production runtime reported `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready`, and schema version 23. The maintainer verified Needs Review, Smart Collections and Archive against the PostgreSQL authority; a missing repository-root setting discovered during cutover was added to the private launcher configuration, after which Archive profile/status and analysed state were healthy.

Rollback acceptance was performed before normal post-cutover edits: PostgreSQL-authoritative Photo Identity was stopped, a writable working copy was made from the preserved final SQLite backup, the launcher was temporarily switched to `sqlite`, and representative application state was verified. The preserved backup itself was not modified. The SQLite rollback runtime was then stopped, the accepted PostgreSQL launcher configuration was restored, and the application returned successfully to PostgreSQL authority with healthy schema-23 `/health` state.

WI-0102 is therefore complete. PostgreSQL is the accepted authoritative catalogue. The preserved SQLite backup remains a rollback/migration artifact rather than an active authority; longer-term PostgreSQL backup/recovery and operational stabilization belong to WI-0106.
