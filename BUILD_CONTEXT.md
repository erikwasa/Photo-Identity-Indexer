# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0097 — Establish PostgreSQL runtime and migration foundation** remains in progress.

PRs #229–#234 are merged. Maintainer verification proves PostgreSQL is healthy and authenticated inside the container, while Windows localhost accepts TCP but fails the PostgreSQL startup protocol. Enabling Podman WSL user-mode networking did not fix the failure.

Current upstream evidence matches a Podman 6.0.x Windows/WSL regression rather than a Photo Identity defect: Podman issue #29377 is open/triaged for Windows localhost port forwarding after upgrading to 6.0.2, and Microsoft WSL issue #41204 separately reports Podman 6 host forwarding broken while Podman 4.x/5.x worked.

The active PR #235 now detects Podman client/server versions and classifies that failure signature explicitly. The known-good Windows/WSL fallback baseline for WI-0097 is Podman 5.8.5; Podman Desktop 1.28.3 shipped that version.

SQLite remains authoritative and untouched. WI-0098 stays blocked until the Windows-host live migration test succeeds.

## Next concrete step

1. Maintainer version is confirmed: Podman client/server 6.0.2, matching the upstream Windows/WSL forwarding regression.
2. Move the local Podman runtime to the known-good 5.8.5 baseline. Podman Desktop 1.28.3 shipped 5.8.5.
3. If the engine downgrade requires a Podman-machine recreation, treat that machine/container/volume state as disposable for WI-0097. Do not touch the SQLite catalogue or any unrelated container workloads without backing them up first.
4. Confirm `podman version` reports client/server 5.8.5, then run `./verify-postgres.ps1`.
5. Verify Photo Identity `/health` reports `catalogueProvider=sqlite` and PostgreSQL `ready` at schema version 1.
6. Complete WI-0097 and begin WI-0098.

Do not use the dynamic Podman-machine IP as permanent application configuration.

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
