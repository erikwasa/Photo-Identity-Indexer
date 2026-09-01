# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0097 — Establish PostgreSQL runtime and migration foundation** remains in progress.

PR #229 established the PostgreSQL/Npgsql foundation. PRs #230 and #231 corrected and diagnosed Windows/WSL port forwarding. The latest maintainer verification now reaches the Windows localhost listener but Npgsql 10 times out in `NpgsqlConnector.SetupEncryption` before authentication.

The active slice `agent/WI-0097-local-npgsql-encryption` makes the local Podman boundary explicit: the repository's PostgreSQL container does not configure TLS or GSS transport, so the generated local/test connection string uses `SSL Mode=Disable;GSS Encryption Mode=Disable`. The persistence layer itself does not override security parameters supplied by external PostgreSQL configuration.

SQLite remains authoritative and untouched. WI-0098 stays blocked until the live Podman migration bootstrap succeeds from Windows.

## Next concrete step

1. Merge the local-Npgsql-encryption corrective PR after CI is green.
2. Run `./verify-postgres.ps1` again.
3. If the isolated database bootstrap passes, set `PhotoIdentity__Postgres__ConnectionString` using the documented local security parameters and verify `/health` reports `catalogueProvider=sqlite` plus PostgreSQL `ready` at schema version 1.
4. Complete WI-0097 and begin WI-0098.

Do not migrate or delete the real SQLite catalogue.

## Relevant files

- docs/decisions/ADR-0009-postgresql-authoritative-catalogue.md
- docs/delivery/work-items/WI-0097-postgresql-runtime-foundation.md
- src/PhotoIdentity.Persistence.Postgres/PostgresCatalogueDatabase.cs
- deploy/postgres/compose.yaml
- verify-postgres.ps1
- docs/operations/postgresql-local-runtime.md
- docs/delivery/status/work-items.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Podman-backed WI-0097 verification:

    ./verify-postgres.ps1
