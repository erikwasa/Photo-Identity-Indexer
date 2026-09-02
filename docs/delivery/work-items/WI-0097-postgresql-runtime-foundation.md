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


## Corrective slice — WSL localhost-forwarding diagnostics

The first port-mapping correction proved insufficient on the maintainer machine: Podman reported `0.0.0.0:5432` and PostgreSQL was healthy, but Windows still refused `127.0.0.1:5432`.

The verifier now diagnoses the host boundary instead of assuming Compose is at fault:

- reports the active WSL networking mode through the Podman machine;
- reads relevant `.wslconfig` values when present;
- checks whether Windows can reach the Podman-machine IPv4 address as a diagnostic only;
- rejects a dynamic machine-IP connection as the production solution; and
- gives targeted remediation for disabled localhost forwarding or a mirrored-networking setup that does not forward the published port.

A stable Windows-localhost endpoint remains an acceptance requirement because the Photo Identity application runs on Windows and the PostgreSQL container address must survive WSL restarts without operator reconfiguration.


## Corrective slice — local Npgsql encryption negotiation

After Windows localhost forwarding became reachable, maintainer verification progressed to Npgsql but timed out in `NpgsqlConnector.SetupEncryption` before authentication. Npgsql 10 defaults SSL and GSS encryption modes to `Prefer`, while the local Podman PostgreSQL runtime does not configure either transport.

The supported local verification/runtime connection therefore explicitly uses:

```text
SSL Mode=Disable;GSS Encryption Mode=Disable
```

This is scoped to the loopback-only local PostgreSQL runtime. The persistence layer does not override security settings supplied by an external production PostgreSQL connection string; a future external/remote deployment can require TLS independently.


## Corrective slice — protocol-level transport isolation

The next maintainer run progressed beyond encryption negotiation but Windows localhost closed the stream during PostgreSQL authentication. This slice separates database state from transport state:

- `pg_isready` remains the liveness check, but an authenticated `psql SELECT 1` inside the container now verifies the persisted credentials.
- Windows sends a minimal PostgreSQL startup packet and requires a PostgreSQL response before xUnit runs.
- The Podman-machine IPv4 address is tested only as a diagnostic path.
- If the direct machine path succeeds while Windows localhost fails, the verifier identifies the WSL relay as the fault boundary and recommends Podman's supported WSL user-mode networking mode.
- The dynamic Podman-machine IP is never accepted as the permanent Photo Identity connection endpoint.

This is intentionally diagnostic and operational hardening; no catalogue data is migrated and SQLite remains authoritative.


## Corrective slice — user-mode networking remediation

Maintainer verification proved:

- authenticated `SELECT 1` succeeds inside the PostgreSQL container;
- Podman publishes `0.0.0.0:5432`;
- Windows can open `127.0.0.1:5432`; and
- a PostgreSQL startup packet sent through Windows localhost does not receive a valid PostgreSQL response while Podman reports `UserModeNetworking=false`.

That is sufficient to classify the failing boundary as the default Windows/WSL network path. The verifier now recommends Podman's supported WSL user-mode networking mode whenever this state is observed, even if the dynamic Podman-machine IP does not answer the diagnostic probe.

The remediation is:

```powershell
podman machine stop
podman machine set --user-mode-networking=true
podman machine start
```

Afterward, rerun `./verify-postgres.ps1`. The dynamic machine IP remains diagnostic-only and is never accepted as the permanent Photo Identity endpoint.


## Corrective slice — Podman 6.0.x upstream regression classification

Maintainer verification with Podman user-mode networking enabled still produced the same protocol failure: authenticated SQL succeeded inside PostgreSQL, but Windows localhost did not carry the PostgreSQL startup protocol.

Current upstream evidence now matches this failure closely:

- Podman issue #29377 is open and triaged as a Windows machine regression after upgrading to Podman 6.0.2; Windows localhost port forwarding fails while the Podman-machine address works.
- Microsoft WSL issue #41204 separately reports Podman 6 port forwarding failing from WSL to Windows while Podman 4.x/5.x worked.

The verifier now prints Podman client/server versions and, when a 6.0.x runtime reaches this exact protocol-failure state, classifies it as the known upstream regression instead of recommending more Photo Identity or PostgreSQL changes.

The known-good Windows/WSL fallback baseline for WI-0097 is Podman 5.8.x. Podman Desktop 1.28.3 ships the 5.8.5 Windows client; the Podman machine may legitimately report a newer 5.8.x Linux server image. Reverting the local container runtime is an environment workaround only; SQLite remains authoritative and no catalogue data is migrated.


### Maintainer environment confirmation

On 2026-09-02 the maintainer confirmed both sides of the active Podman machine are exactly **6.0.2**:

- Windows client: Podman 6.0.2, commit `b28edb9ad70ce4317dc762ee9ce0a6d081d154e9`.
- Linux server: Podman 6.0.2, the same commit.

This is the same Podman release and commit family reported in upstream issue #29377. WI-0097 therefore treats the current localhost transport failure as an environment/runtime blocker, not a Photo Identity catalogue defect. The next verification should use the Podman 5.8.x Windows/WSL baseline.


### Maintainer 5.8.x baseline confirmation

After recreating/downgrading the runtime on 2026-09-02, the maintainer confirmed:

- Windows client: Podman 5.8.5, commit `6d48b6f12f793176f3f6bc808b5a440984c14eb2`.
- Linux server: Podman 5.8.6, commit `a859fc66702c23e869c282c63e92d9b6cd264229`.

This is accepted as the WI-0097 5.8.x baseline. The next step is to rerun `./verify-postgres.ps1` unchanged and observe whether Windows localhost now carries the PostgreSQL protocol correctly.


## Corrective slice — verifier exit-code handling

After moving to the accepted Podman 5.8.x baseline, the maintainer reached:

```text
Authenticated PostgreSQL check inside container passed.
Windows localhost PostgreSQL protocol check passed.
```

but the verifier immediately fell through to its generic failure without showing any xUnit output.

The cause is PowerShell pipeline semantics in `Invoke-LivePostgresTest`: assigning the function result to `$testExitCode` captured both the native `dotnet test` standard output and the explicit `$LASTEXITCODE`, so the caller received an array rather than a single integer. The corrective slice routes test output to the host and returns only the numeric exit code.

This is a verifier-only defect. The successful protocol preflight means the Podman 5.8.x Windows localhost transport is working.
