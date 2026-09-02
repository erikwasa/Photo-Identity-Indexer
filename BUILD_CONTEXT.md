# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0098 — Add database-neutral persistence boundary and foundational PostgreSQL schema** is in progress.

WI-0097 is maintainer-verified. PR #237 merged the first WI-0098 slice, including the focused `IPhotoCaptureMetadataRepository` boundary and PostgreSQL schema version 2.

Maintainer live verification of schema version 2 failed with PostgreSQL SQLSTATE `42601`. The merged trigger function contained invalid `AS $ ... $;` syntax. Because migrations run transactionally, the failed version-2 attempt rolled back and was not recorded as applied.

The active corrective branch is `agent/WI-0098-postgres-trigger-syntax`. It removes PostgreSQL dollar quoting from the trigger function and updates `verify-postgres.ps1` so live migration failures are no longer mislabeled as Podman/WSL networking failures.

SQLite remains authoritative. No PostgreSQL cutover or authoritative writes are enabled.

## Next concrete step

1. Merge the corrective WI-0098 PR after CI is green.
2. Pull current main and run `./verify-postgres.ps1` on the existing Podman 5.8.x runtime. Do not reset the PostgreSQL volume.
3. Confirm the live bootstrap passes and PostgreSQL schema version 2 is recorded.
4. Continue WI-0098 with the durable processing lease/checkpoint/retry persistence boundary.
5. Preserve SQLite authority until controlled cutover in WI-0102.

## Relevant files

- docs/delivery/work-items/WI-0098-persistence-boundary-foundational-schema.md
- src/PhotoIdentity.Core/Sources/IPhotoCaptureMetadataRepository.cs
- src/PhotoIdentity.Persistence.Sqlite/SqliteAssetCatalogueRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresCatalogueDatabase.cs
- tests/PhotoIdentity.Persistence.Tests/PostgresCatalogueDatabaseTests.cs
- verify-postgres.ps1
- docs/delivery/status/work-items.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Live PostgreSQL migration verification:

    ./verify-postgres.ps1
