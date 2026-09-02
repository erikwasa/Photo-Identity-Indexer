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
