---
id: WI-0098
title: Add database-neutral persistence boundary and foundational PostgreSQL schema
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0097]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, tests]
---

# WI-0098: Add database-neutral persistence boundary and foundational PostgreSQL schema

## Objective
Stop application/domain code from depending directly on SQLite implementation types and implement the foundational catalogue/job model in PostgreSQL.

## In scope
- Introduce interfaces/contracts at the application boundary for foundational catalogue and processing persistence.
- Remove direct construction of SQLite repositories from affected application services.
- Create PostgreSQL schema/migrations for sources, assets, revisions, face occurrences/observations/crops, embeddings, processing runs/jobs and required reference data.
- Preserve stable identifiers, uniqueness rules, foreign-key semantics and immutable-revision behavior.
- Keep SQLite adapters behind the same contracts while migration is incomplete.
- Add contract/integration tests that run the same behavioral expectations against both adapters where practical.

## Acceptance criteria
- [ ] Foundational application paths depend on persistence contracts rather than `Sqlite*` concrete types.
- [ ] PostgreSQL schema represents existing foundational entities without lossy type conversions.
- [ ] Processing lease/idempotency/restart semantics remain intact.
- [ ] Existing SQLite behavior stays green during the transition.
- [ ] New authoritative persistence work follows the PostgreSQL-capable boundary.


## Implementation progress

### Slice 1 — neutral asset boundary and PostgreSQL foundational schema

Started 2026-09-02.

- Added focused `IPhotoCaptureMetadataRepository` in Core and placed the existing SQLite capture-metadata operations behind that contract.
- Changed `PhotoMetadataInspectionService` to depend on the neutral capture-metadata contract rather than `SqliteAssetCatalogueRepository`.
- Added PostgreSQL schema version 2 for sources, assets, immutable revisions, face occurrences/observations/crops, embeddings, processing runs, and processing jobs.
- PostgreSQL types preserve identifiers and data shape with `uuid`, `timestamptz`, `jsonb`, `bytea`, explicit checks, uniqueness constraints, foreign keys, and an immutable revision update guard.
- Extended the live PostgreSQL bootstrap test to verify the foundational tables/types and immutable-revision behavior.
- SQLite remains the active/authoritative adapter; this slice adds no dual writes or cutover behavior.

Subsequent WI-0098 slices will move processing and additional foundational application paths behind neutral contracts and add same-behavior adapter tests where practical.


### Corrective slice — neutral contract layering

Workflow #1402 exposed that the first draft of the Core contract referenced `CatalogueSource`, `CatalogueAsset` and `CatalogueAssetRevision`, which still live in the SQLite adapter assembly. That violated the intended dependency direction and did not compile.

The corrective change narrows slice 1 to the application capability actually being decoupled now: capture metadata persistence. `IPhotoCaptureMetadataRepository` depends only on Core-owned types (`AssetRevisionId` and `PhotoCaptureMetadata`). Broader catalogue records will move behind neutral contracts deliberately in later WI-0098 slices rather than being pulled across namespaces as an incidental compile fix.


### Corrective slice — PostgreSQL trigger syntax

Maintainer live verification after merging PR #237 reached PostgreSQL schema version 2 migration execution, but PostgreSQL rejected the revision-identity trigger function with SQLSTATE `42601` because merged main contained an invalid single-dollar delimiter (`AS $ ... $;`).

The migration runs inside the existing transaction, so the failed schema-version-2 attempt is rolled back and version 2 is not recorded as applied. No PostgreSQL reset is required.

The corrective slice replaces dollar quoting entirely with an ordinary single-quoted PL/pgSQL function body and doubles the embedded exception-message quotes. The verifier's terminal failure message is also corrected so a live migration/test failure is no longer mislabeled as a Podman/WSL networking failure.


### Maintainer verification — PostgreSQL schema version 2

After PR #239 merged on 2026-09-02, the maintainer reran `verify-postgres.ps1` against the existing Podman 5.8.x PostgreSQL volume.

Verification passed end-to-end:

- authenticated SQL inside the container passed;
- Windows localhost PostgreSQL protocol passed;
- the isolated live migration test passed;
- schema version 2 applied successfully without resetting the PostgreSQL volume.

This verifies the foundational PostgreSQL schema and the corrected revision-identity trigger syntax.


### Slice 2 — durable processing execution boundary

Started 2026-09-02.

- Moved durable processing run/job records and statuses from the SQLite assembly into `PhotoIdentity.Core.Processing`; they describe provider-neutral execution state rather than SQLite implementation details.
- Moved `ProcessingLeaseLostException` into Core so lease invalidation is part of the persistence contract rather than an SQLite-specific exception.
- Added `IProcessingExecutionRepository` for claim, summary, checkpoint, completion, failure/retry and run-finalization operations used by the resumable worker.
- `SqliteProcessingRepository` implements the neutral contract with its existing transaction/lease semantics.
- `ResumableBatchProcessor` now depends on `IProcessingExecutionRepository` and no longer references the SQLite persistence namespace.
- Existing SQLite processing tests remain the behavior guard for lease, retry, checkpoint, idempotency and restart semantics. A PostgreSQL implementation remains for the later persistence-migration work item rather than introducing dual writes here.


### Slice 3 — application asset-revision lookup boundary

Started 2026-09-02.

- Added Core-owned `AssetRevisionLookup`, `IAssetRevisionStorageDescriptor` and `IAssetRevisionLookupRepository` so application services can resolve immutable revisions and their source locations without depending on SQLite types.
- `SqliteLocalBatchRepository` now implements the neutral lookup contract by projecting its existing revision read model; its established concrete API remains available for migration-era callers.
- `CollectionPhotoFileResolver`, `CollectionOriginalAccessService` and `SlideshowOriginalPreparationService` now depend on the neutral lookup contract.
- Hydration admission accepts the Core storage descriptor so both the new neutral projection and existing migration-era SQLite revision records preserve the same byte-budget behavior.
- API dependency injection binds `IAssetRevisionLookupRepository` to the existing SQLite singleton. SQLite remains authoritative; this slice adds no PostgreSQL reads, writes or dual-write behavior.
- Focused integration tests verify SQLite lookup-by-id, lookup-by-source/hash and missing-revision behavior through the Core contract.

Later WI-0098 slices may move the broader source/asset catalogue records behind neutral contracts. Archive/background writes, review/identity persistence and library persistence remain owned by WI-0099, WI-0100 and WI-0101 respectively.


### Slice 4 — processing run lifecycle boundary

Started 2026-09-02.

- Added `IProcessingRunRepository` in Core for durable run creation, run lookup, queued-job inspection and cancellation.
- `SqliteProcessingRepository` implements both `IProcessingRunRepository` and `IProcessingExecutionRepository`; the existing SQLite transactions and lease invalidation behavior are unchanged.
- `LocalBatchCoordinator` now receives the run-lifecycle and execution contracts instead of constructing `SqliteProcessingRepository` internally.
- The batch CLI remains the composition root: it constructs the SQLite adapter once, then passes it through the neutral lifecycle/execution interfaces for start, resume, status, cancellation and failure reporting.
- Added focused integration coverage that exercises run creation, lookup, queued-job retrieval and cancellation through `IProcessingRunRepository`.
- SQLite remains authoritative. This slice introduces no PostgreSQL processing reads/writes or dual writes.

The remaining local-batch catalogue scan/source-registration dependency is still SQLite-specific and is a candidate for the next WI-0098 foundational boundary. Archive/background processing migration remains WI-0099 scope.
