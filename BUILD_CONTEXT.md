# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0097 — Establish PostgreSQL runtime and migration foundation** is in progress on branch `agent/WI-0097-postgresql-runtime-foundation`.

PostgreSQL remains the selected long-term authoritative catalogue under ADR-0009. This first implementation slice adds the migration target without changing current production semantics: SQLite remains authoritative and all existing catalogue reads/writes still use SQLite.

The branch adds a separate Npgsql persistence project, PostgreSQL 18 Podman Compose runtime, versioned migration-history bootstrap, optional `PhotoIdentity__Postgres__ConnectionString` configuration, PostgreSQL state in `/health`, and a Podman-backed isolated-database verification script.

Comparative SQLite/PostgreSQL benchmarks are not required. The goal remains safe forward progress toward migrating the existing catalogue and completing the full archive.

M22 maintainer verification remains separate. M23 is not a prerequisite for M24.

## Next concrete step

1. Run normal PR CI with PostgreSQL unconfigured; existing SQLite operation must remain green.
2. Run `./verify-postgres.ps1` on the maintainer WSL2/Podman Desktop environment using a private `deploy/postgres/.env`.
3. Start Photo Identity with `PhotoIdentity__Postgres__ConnectionString` supplied externally and verify `/health` reports `catalogueProvider=sqlite` plus PostgreSQL `ready` at schema version 1.
4. Correct any portability/CI findings, then complete WI-0097 and begin WI-0098.

Do not migrate or delete the real SQLite catalogue in WI-0097.

## Relevant files

- docs/decisions/ADR-0009-postgresql-authoritative-catalogue.md
- docs/delivery/work-items/WI-0097-postgresql-runtime-foundation.md
- src/PhotoIdentity.Persistence.Postgres/PostgresCatalogueDatabase.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresCatalogueHealth.cs
- deploy/postgres/compose.yaml
- deploy/postgres/.env.example
- verify-postgres.ps1
- docs/operations/postgresql-local-runtime.md
- docs/delivery/status/work-items.yaml
- docs/delivery/status/milestones.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Podman-backed WI-0097 verification:

    ./verify-postgres.ps1
