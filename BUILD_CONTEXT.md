# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0097 — Establish PostgreSQL runtime and migration foundation** remains in progress.

PRs #229–#232 are merged. The latest maintainer verification reaches PostgreSQL authentication through Windows localhost but the stream closes before authentication completes. The active branch is `agent/WI-0097-postgres-protocol-diagnostics`.

The verifier now isolates four boundaries: PostgreSQL liveness, authenticated SQL inside the container, PostgreSQL protocol response through Windows localhost, and the full Npgsql migration test. It probes the Podman-machine address only for diagnosis. If the machine address works while localhost fails and Podman WSL user-mode networking is disabled, the verifier recommends enabling Podman's supported user-mode networking rather than using an unstable machine IP.

SQLite remains authoritative and untouched. WI-0098 stays blocked until the Windows-host live migration test succeeds.

## Next concrete step

1. Merge the protocol-diagnostic corrective PR after CI is green.
2. Run `./verify-postgres.ps1` again.
3. Follow the exact remediation reported by the verifier. If it identifies a WSL relay failure with user-mode networking disabled, run:
   `podman machine stop`
   `podman machine set --user-mode-networking=true`
   `podman machine start`
4. Rerun verification until the isolated PostgreSQL database/bootstrap test passes through Windows localhost.
5. Verify Photo Identity `/health` reports `catalogueProvider=sqlite` and PostgreSQL `ready` at schema version 1.
6. Complete WI-0097 and begin WI-0098.

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
