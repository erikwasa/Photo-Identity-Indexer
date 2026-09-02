# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0098 — Add database-neutral persistence boundary and foundational PostgreSQL schema** is in progress.

WI-0097 was maintainer-verified on 2026-09-02. On the accepted Podman 5.8.x Windows/WSL baseline, `verify-postgres.ps1` passed end-to-end and current main `/health` reported the real SQLite catalogue as authoritative at schema version 16 plus PostgreSQL `ready` at schema version 1.

The active WI-0098 branch is `agent/WI-0098-persistence-boundary-foundational-schema`.

Slice 1 introduces `IPhotoCaptureMetadataRepository`, places the SQLite capture-metadata operations behind it for application DI, and moves `PhotoMetadataInspectionService` off the SQLite concrete type. PostgreSQL schema version 2 adds sources, assets, immutable revisions, face occurrences/observations/crops, embeddings and durable processing run/job tables with PostgreSQL-native constraints/types. Live bootstrap tests verify those tables and immutable revision behavior.

SQLite remains authoritative. There are no PostgreSQL authoritative writes or cutover behavior in WI-0098 slice 1.

## Next concrete step

1. Merge the first WI-0098 slice after CI is green.
2. Run `./verify-postgres.ps1` once against the existing Podman 5.8.x runtime to apply/verify PostgreSQL schema version 2.
3. Continue WI-0098 by moving the durable processing lease/checkpoint/retry boundary behind a neutral contract and then other foundational application services.
4. Preserve the existing SQLite behavior until controlled cutover in WI-0102.

## Relevant files

- docs/delivery/work-items/WI-0098-persistence-boundary-foundational-schema.md
- src/PhotoIdentity.Core/Sources/IPhotoCaptureMetadataRepository.cs
- src/PhotoIdentity.Persistence.Sqlite/SqliteAssetCatalogueRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresCatalogueDatabase.cs
- tests/PhotoIdentity.Persistence.Tests/PostgresCatalogueDatabaseTests.cs
- docs/delivery/status/work-items.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Live PostgreSQL migration verification:

    ./verify-postgres.ps1
