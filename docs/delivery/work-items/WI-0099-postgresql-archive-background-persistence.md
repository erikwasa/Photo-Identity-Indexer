---
id: WI-0099
title: Migrate archive and background-processing persistence to PostgreSQL
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0098]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Worker, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Sqlite]
---

# WI-0099: Migrate archive and background-processing persistence to PostgreSQL

## Objective
Move the high-concurrency archive/background writer domains to PostgreSQL and remove the host-shutdown failure mode seen under SQLite contention.

## In scope
- Implement PostgreSQL persistence for archive coverage, observations, availability, verification, analysis/post-analysis, hydration, storage and advancement control.
- Migrate automatic Places enrichment operational state needed by its hosted worker.
- Preserve resumability, idempotency and OneDrive hydration/release ownership semantics.
- Add top-level hosted-service resilience so transient database failures are logged/retried and failure-reporting writes cannot escape and terminate the host.
- Ensure one background worker failure does not silently stop all future work.
- Add concurrency-focused integration tests with archive advancement, enrichment and another writer active together.

## Acceptance criteria
- [ ] Archive/background state can run entirely against PostgreSQL.
- [ ] Concurrent background writes do not produce the prior SQLite table-lock shutdown class.
- [ ] A transient database exception cannot terminate Photo Identity through an escaping recovery write.
- [ ] Durable run/lease/retry state survives application restart.
- [ ] No personal paths/content are emitted by new diagnostics beyond existing privacy-safe conventions.


## Implementation progress

### Slice 1 — PostgreSQL archive-analysis state

Started 2026-09-02.

- Added provider-neutral `IArchiveAnalysisStateRepository` for exact analysis-profile registration, run/profile lookup and successful immutable-revision completion state.
- `SqliteArchiveAnalysisRepository` implements the state contract while retaining its existing pending/current-revision selection API for the still-authoritative SQLite runtime.
- Added PostgreSQL schema version 3 with `archive_analysis_profiles`, `archive_analysis_runs` and `asset_revision_analysis`, preserving the existing profile hash, processing-run and immutable-revision relationships with PostgreSQL-native UUID/timestamp constraints.
- Added `PostgresArchiveAnalysisStateRepository` implementing profile registration, profile lookup, completion lookup and idempotent completion recording.
- Extended the existing live PostgreSQL verification to prove schema version 3 plus register → lookup → completion behavior.
- Runtime archive selection/coverage/availability and processing execution remain on SQLite in this slice. No dual writes or mixed-authority runtime path are introduced.

The next WI-0099 slices can migrate archive coverage/source observations/availability and durable processing execution before switching the archive runtime to PostgreSQL.


### Maintainer verification — PostgreSQL schema version 3

After PR #245 merged on 2026-09-02, the maintainer reran `verify-postgres.ps1` against the existing Podman 5.8.x PostgreSQL volume.

Verification passed end-to-end:
- authenticated SQL inside the container passed;
- Windows localhost PostgreSQL protocol passed;
- the isolated live PostgreSQL persistence test passed;
- schema version 3 and the archive-analysis register → lookup → completion behavior were accepted without resetting the volume.

### Slice 2 — PostgreSQL archive-asset availability

Started 2026-09-02.

- Added provider-neutral `IArchiveAvailabilityRepository` in Core for the per-asset last-observed availability write.
- `SqliteArchiveAvailabilityRepository` now implements the same contract without changing existing SQLite behavior.
- Added PostgreSQL schema version 4 with `archive_asset_availability`, PostgreSQL-native UUID/timestamptz types, allowed-state checks, asset foreign key and availability index.
- Added `PostgresArchiveAvailabilityRepository` with idempotent upsert behavior matching the existing SQLite semantics.
- Extended the isolated live PostgreSQL test to prove schema version 4 and availability overwrite behavior.
- Runtime archive scanning/status/hydration remains SQLite-authoritative; this slice adds no dual writes or mixed-authority runtime reads.

The next slice should move the lightweight archive source-observation/verification state onto a provider-neutral boundary, reusing the availability table established here.


### Maintainer verification — PostgreSQL schema version 4

After PR #246 merged on 2026-09-02, the maintainer reran `verify-postgres.ps1` on main at merge commit `740dfd9684dc83ac77728beec206dc5950fac49a`.

Verification passed end-to-end on the existing Podman 5.8.x volume:
- authenticated PostgreSQL SQL inside the container passed;
- the Windows localhost PostgreSQL protocol check passed;
- the isolated live persistence test passed;
- schema version 4 and archive-availability upsert behavior were accepted without resetting the volume.

### Slice 3 — PostgreSQL source observation and verification baseline

Started 2026-09-02.

- Added provider-neutral `IArchiveSourceObservationRepository` plus Core-owned source/observation/result DTOs.
- `SqliteArchiveSourceObservationRepository` implements the new contract through explicit compatibility adapters, leaving existing SQLite callers and record types unchanged.
- Added PostgreSQL schema version 5 with `archive_source_observations`, verification-state constraints, optional verified-revision baseline metadata, foreign keys and the pending-verification index.
- Added `PostgresArchiveSourceObservationRepository` preserving the current safety semantics: metadata divergence may require verification; metadata alone never establishes a new immutable revision; once verification is required, only verified content clears it.
- The PostgreSQL adapter updates source, asset, availability, observation and optional verified revision in a single transaction matching the current SQLite write boundary.
- Extended the isolated live PostgreSQL test through the transition sequence: existing revision without baseline → needs verification → verified content → unchanged metadata remains verified → changed metadata returns to needs verification.
- Runtime archive scanning, verification scheduling and status remain SQLite-authoritative. No dual writes or mixed-authority reads are introduced.

After this slice, archive coverage and durable processing execution remain the major prerequisites before any archive runtime cutover.


### Schema version 5 live-verification correction

After PR #247 merged and workflow #1444 passed, the maintainer reran `verify-postgres.ps1`. PostgreSQL connectivity remained healthy, but the isolated live persistence test failed with PostgreSQL check-constraint error 23514 on `assets`.

The failure was test-fixture chronology rather than a runtime persistence defect: the fixture seeded `assets.created_at_utc` with the current clock, then replayed a fixed earlier source-observation timestamp. The existing foundational constraint correctly rejects `last_seen_at_utc < created_at_utc`.

The corrective slice keeps the production constraint unchanged and makes the live fixture use one fixed chronological baseline for source/asset/revision creation, availability checks and source observations. Schema version 5 remains pending maintainer acceptance until `verify-postgres.ps1` passes after the corrective PR.


### Maintainer verification — PostgreSQL schema version 5

After corrective PR #248 merged on 2026-09-02, the maintainer reran `verify-postgres.ps1`.

Verification passed end-to-end on the existing Podman 5.8.x volume:
- authenticated PostgreSQL SQL inside the container passed;
- the Windows localhost PostgreSQL protocol check passed;
- the isolated live PostgreSQL persistence test passed;
- schema version 5 and source-observation/verification semantics were accepted without resetting the volume.

### Slice 4 — PostgreSQL archive coverage

Started 2026-09-02.

- Added provider-neutral `IArchiveCoverageRepository` and Core-owned `ArchiveCoverageState`.
- `SqliteArchiveCoverageRepository` implements the new contract through explicit compatibility adapters while retaining its existing public APIs.
- Added PostgreSQL schema version 6 with singleton `archive_configuration` and `archive_included_folders`, using UUID/timestamptz relationships to the foundational `sources` table.
- Added `PostgresArchiveCoverageRepository` preserving the one-permanent-source rule and existing recursive-folder normalization/collapse behavior.
- Extended the isolated live PostgreSQL test to cover initial configuration, additive includes, parent-folder collapse, replacement and persisted readback.
- Runtime archive endpoints, scanner, status, hydration and advancement services remain SQLite-authoritative. No dual writes or mixed-authority runtime reads are introduced.

After this slice, durable processing execution remains the major persistence prerequisite before a controlled archive runtime cutover can be designed.


### Maintainer verification — PostgreSQL schema version 6

After PR #249 merged on 2026-09-02, the maintainer reran `verify-postgres.ps1`.

Verification passed on the existing Podman 5.8.x PostgreSQL volume, accepting schema version 6 and the archive-coverage persistence behavior without resetting the volume.

### Slice 5 — PostgreSQL durable processing execution

Started 2026-09-02.

- Added `PostgresProcessingRepository` implementing both existing Core boundaries: `IProcessingRunRepository` and `IProcessingExecutionRepository`.
- Reused the foundational PostgreSQL `processing_runs` / `processing_jobs` schema introduced in schema version 2; no new migration is required for this slice.
- Preserved pending-run/idempotent-job creation, cancellation and terminal run-finalization semantics.
- Added atomic PostgreSQL job claiming with `FOR UPDATE ... SKIP LOCKED` so competing workers cannot lease the same job.
- Preserved lease tokens, lease expiry, expired-running-job reclaim, checkpoint JSON, transient retry scheduling, permanent failure and stale-token rejection.
- Added live PostgreSQL verification for repeated run creation, checkpoint persistence, stale lease rejection, retry/reclaim after a new repository instance, competing claims and cancellation.
- Runtime archive orchestration remains SQLite-authoritative. This slice does not introduce dual writes or switch a running archive worker to PostgreSQL.

After this repository is accepted, WI-0099 should reassess the remaining archive-specific persistence surfaces and then design one controlled PostgreSQL runtime composition change rather than introducing mixed authority incrementally.
