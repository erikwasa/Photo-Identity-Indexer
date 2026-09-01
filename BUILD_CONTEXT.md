# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0097 — Establish PostgreSQL runtime and migration foundation** remains in progress.

PRs #229–#234 are merged. Maintainer verification now proves PostgreSQL is healthy and authenticated inside the container, while Windows localhost accepts TCP but fails the PostgreSQL startup protocol. Podman reports `UserModeNetworking=false`, so the failing boundary is the default Windows/WSL network path rather than PostgreSQL, credentials or Npgsql.

The active branch is `agent/WI-0097-user-mode-networking-remediation`. The verifier now recommends Podman's supported WSL user-mode networking mode directly for this state rather than requiring the dynamic Podman-machine IP diagnostic to succeed.

SQLite remains authoritative and untouched. WI-0098 stays blocked until the Windows-host live migration test succeeds.

## Next concrete step

1. On the maintainer machine run:
   `podman machine stop`
   `podman machine set --user-mode-networking=true`
   `podman machine start`
2. Rerun `./verify-postgres.ps1`.
3. If the isolated PostgreSQL bootstrap passes, verify Photo Identity `/health` with the documented local connection string.
4. Complete WI-0097 and begin WI-0098.

Enabling Podman WSL user-mode networking can affect other active WSL distributions while the Podman machine is running because WSL distributions share the kernel/networking layer. Stopping the last Podman machine using user-mode networking restores the original WSL network path.

Do not migrate or delete the real SQLite catalogue. Do not use the dynamic Podman-machine IP as permanent application configuration.

## Relevant files

- docs/decisions/ADR-0009-postgresql-authoritative-catalogue.md
- docs/delivery/work-items/WI-0097-postgresql-runtime-foundation.md
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
