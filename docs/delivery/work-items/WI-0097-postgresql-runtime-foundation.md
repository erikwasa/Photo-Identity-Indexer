---
id: WI-0097
title: Establish PostgreSQL runtime and migration foundation
milestone: M24
status_source: ../status/work-items.yaml
depends_on: []
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Api, packaging, operations, tests]
---

# WI-0097: Establish PostgreSQL runtime and migration foundation

## Objective
Provide a supported local PostgreSQL runtime for Photo Identity using Podman/WSL2, plus application configuration, schema migration and health checks.

## In scope
- Add PostgreSQL/Npgsql dependencies and connection configuration without embedding credentials in source.
- Add a Podman-compatible compose/container definition with pinned PostgreSQL major version and persistent named storage.
- Add database creation/schema migration infrastructure suitable for automated tests and upgrades.
- Add startup/health diagnostics that distinguish database unavailable, authentication failure and migration failure.
- Document start/stop/reset rules for development and operator use.
- Keep SQLite as the current production catalogue until later cutover work.

## Acceptance criteria
- [ ] A clean machine with WSL2/Podman prerequisites can start the defined PostgreSQL service and retain data across container restart.
- [ ] Photo Identity can connect using configuration supplied outside source control.
- [ ] An empty PostgreSQL catalogue can be initialized and schema migrations are versioned/idempotent.
- [ ] Health output reports PostgreSQL connectivity/schema state without exposing secrets.
- [ ] Automated tests can provision an isolated PostgreSQL database/container-compatible connection.
- [ ] No existing SQLite catalogue is modified by this work item.


## Implementation slice 1 — runtime bootstrap

The first implementation slice establishes PostgreSQL without changing the authoritative catalogue:

- `PhotoIdentity.Persistence.Postgres` owns an Npgsql data source and versioned migration-history bootstrap.
- PostgreSQL configuration is optional through `PhotoIdentity__Postgres__ConnectionString`; no repository file contains a real connection string or credential.
- API startup attempts PostgreSQL initialization only when configured and keeps SQLite authoritative whether PostgreSQL is absent, unavailable or fails bootstrap.
- `/health` reports `not_configured`, `ready`, `unavailable`, `authentication_failed` or `migration_failed` without returning exception text or connection details.
- `deploy/postgres/compose.yaml` pins PostgreSQL major version 18, binds only to loopback and persists data in a named volume.
- `verify-postgres.ps1` starts the Podman service from private `.env` values, waits for readiness, provisions an isolated disposable database, applies the migration bootstrap twice and then drops the test database.
- The ordinary persistence test suite also verifies that an unreachable PostgreSQL endpoint is classified without affecting SQLite operation.

PostgreSQL schema version 1 intentionally contains only migration-history infrastructure. Foundational catalogue tables remain WI-0098 scope.

## Verification

Automated repository CI must pass with PostgreSQL not configured; this proves WI-0097 does not add a new requirement for existing SQLite operation.

Podman-backed verification on a configured operator/development machine:

```powershell
Copy-Item .\deploy\postgres\.env.example .\deploy\postgres\.env
# Replace only the private password placeholder in .env.
./verify-postgres.ps1
```

After that succeeds, set `PhotoIdentity__Postgres__ConnectionString` in the process environment and start Photo Identity. Confirm `/health` still reports `catalogueProvider: sqlite` and reports PostgreSQL `ready` with schema version 1.

Do not point this slice at a replacement production catalogue or remove the existing SQLite file.


## Corrective slice — Windows Podman port forwarding

Maintainer verification after PR #229 merged proved PostgreSQL was healthy inside the container but Windows could not connect to `127.0.0.1:5432`. The initial Compose mapping explicitly bound `127.0.0.1` inside the Podman WSL machine. The corrective slice removes that VM-loopback bind so Podman's Windows port forwarding can expose the published port on Windows localhost.

`verify-postgres.ps1` now verifies both boundaries separately:

1. PostgreSQL readiness inside the container with `pg_isready`.
2. Windows-host TCP reachability on the configured localhost port before xUnit starts.

This prevents a WSL/Podman forwarding defect from being misreported as a PostgreSQL schema/bootstrap failure.
