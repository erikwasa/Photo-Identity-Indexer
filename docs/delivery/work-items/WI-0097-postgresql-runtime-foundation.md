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
