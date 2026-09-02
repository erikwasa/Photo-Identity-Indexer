# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0097 — Establish PostgreSQL runtime and migration foundation** remains in progress.

The maintainer is now on the accepted Podman 5.8.x Windows/WSL baseline: client 5.8.5 and server 5.8.6. On that runtime, PostgreSQL authentication succeeds inside the container and the Windows localhost PostgreSQL protocol preflight passes, confirming the Podman 6.0.x forwarding regression is avoided.

The remaining failure is in `verify-postgres.ps1` itself. `Invoke-LivePostgresTest` emitted normal `dotnet test` output into the PowerShell pipeline and then returned `$LASTEXITCODE`; assigning the function result captured all of that as an array, so the caller did not receive a scalar integer exit code. The active branch `agent/WI-0097-verifier-exit-code` sends test output to the host and returns only the numeric exit code.

SQLite remains authoritative and untouched. WI-0098 stays blocked until the live migration bootstrap and application health check pass.

## Next concrete step

1. Merge the verifier-exit-code corrective PR after CI is green.
2. Run `./verify-postgres.ps1` again on the existing Podman 5.8.x runtime.
3. If xUnit passes and the script reports `PostgreSQL runtime verification passed.`, configure Photo Identity with the documented local PostgreSQL connection string.
4. Verify `/health` reports `catalogueProvider=sqlite`, PostgreSQL `status=ready`, and `schemaVersion=1`.
5. Complete WI-0097 and begin WI-0098.

Do not migrate or delete the real SQLite catalogue.

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
