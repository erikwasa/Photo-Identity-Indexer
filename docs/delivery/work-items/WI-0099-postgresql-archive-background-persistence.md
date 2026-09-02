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
