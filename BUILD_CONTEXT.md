# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0097 — Establish PostgreSQL runtime and migration foundation** remains in progress.

PRs #229–#234 are merged. Maintainer verification proves PostgreSQL is healthy and authenticated inside the container, while Windows localhost accepts TCP but fails the PostgreSQL startup protocol. Enabling Podman WSL user-mode networking did not fix the failure.

Current upstream evidence matches a Podman 6.0.x Windows/WSL regression rather than a Photo Identity defect: Podman issue #29377 is open/triaged for Windows localhost port forwarding after upgrading to 6.0.2, and Microsoft WSL issue #41204 separately reports Podman 6 host forwarding broken while Podman 4.x/5.x worked.

The active PR #235 now detects Podman client/server versions and classifies that failure signature explicitly. The maintainer has moved to the accepted Podman 5.8.x baseline: Windows client 5.8.5 and Linux server 5.8.6.

SQLite remains authoritative and untouched. WI-0098 stays blocked until the Windows-host live migration test succeeds.

## Next concrete step

1. Podman downgrade/recreation is complete: client 5.8.5, server 5.8.6. This 5.8.x patch skew is accepted.
2. Run `./verify-postgres.ps1` unchanged.
3. If the isolated PostgreSQL bootstrap succeeds through Windows localhost, verify Photo Identity `/health` reports `catalogueProvider=sqlite` and PostgreSQL `ready` at schema version 1.
4. Complete WI-0097 and begin WI-0098.

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
