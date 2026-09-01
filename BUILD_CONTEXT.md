# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0097 — Establish PostgreSQL runtime and migration foundation** remains in progress.

PR #229 established the PostgreSQL 18/Npgsql migration foundation and merged green. PR #230 corrected the initial Compose port bind and also merged green, but maintainer verification still found PostgreSQL healthy inside Podman while Windows `127.0.0.1:5432` refused the connection.

The active corrective slice is `agent/WI-0097-wsl-forwarding-diagnostics`. It treats this as a WSL host-forwarding issue rather than a PostgreSQL/schema failure: the verifier reports WSL networking mode, relevant `.wslconfig` settings and Podman-machine IP reachability before recommending a stable localhost fix.

SQLite remains authoritative and untouched. Do not begin WI-0098 until Windows can reliably reach PostgreSQL through a stable localhost endpoint and the migration bootstrap passes against the live container.

## Next concrete step

1. Merge the WSL-forwarding diagnostic PR after CI is green.
2. Run `./verify-postgres.ps1` again on the maintainer machine.
3. Apply the targeted WSL networking remediation reported by the script if localhost forwarding is disabled or mirrored networking is failing.
4. Rerun verification until the disposable PostgreSQL database test passes.
5. Then verify Photo Identity `/health` reports `catalogueProvider=sqlite` and PostgreSQL `ready` at schema version 1.
6. Complete WI-0097 and begin WI-0098.

Do not use the dynamic Podman-machine IP as the permanent application connection string; it may change after WSL restart.

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
