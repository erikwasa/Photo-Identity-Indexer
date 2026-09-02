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

- Added `IAssetCatalogueRepository` in Core and placed the existing SQLite asset catalogue adapter behind that contract.
- Changed `PhotoMetadataInspectionService` to depend on the neutral contract rather than `SqliteAssetCatalogueRepository`.
- Added PostgreSQL schema version 2 for sources, assets, immutable revisions, face occurrences/observations/crops, embeddings, processing runs, and processing jobs.
- PostgreSQL types preserve identifiers and data shape with `uuid`, `timestamptz`, `jsonb`, `bytea`, explicit checks, uniqueness constraints, foreign keys, and an immutable revision update guard.
- Extended the live PostgreSQL bootstrap test to verify the foundational tables/types and immutable-revision behavior.
- SQLite remains the active/authoritative adapter; this slice adds no dual writes or cutover behavior.

Subsequent WI-0098 slices will move processing and additional foundational application paths behind neutral contracts and add same-behavior adapter tests where practical.
