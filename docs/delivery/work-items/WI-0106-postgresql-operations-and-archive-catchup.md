---
id: WI-0106
title: Operationalize PostgreSQL and resume full-archive catch-up
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0102, WI-0103, WI-0104, WI-0105, WI-0108]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Api, launcher, packaging, operations, documentation]
---

# WI-0106: Operationalize PostgreSQL and resume full-archive catch-up

## Objective
Make PostgreSQL routine to operate on the maintainer machine, then use the migrated system to continue the full existing archive toward steady-state daily updates.

## In scope
- Integrate database availability with the one-click launcher/operator diagnostics without hiding container failures.
- Document Podman Desktop/WSL2 startup, persistent volume location/ownership, controlled shutdown and database upgrade procedure.
- Add backup and restore procedures and verify a restore into an isolated database before relying on PostgreSQL as the only writable catalogue.
- Define safe application/database startup ordering and clear recovery guidance after PC/container/application restart.
- Resume archive advancement on the real migrated catalogue and use application metrics to identify any remaining blocker rather than requiring comparative benchmark runs.
- Verify ongoing synchronization/analyze/enrich/review behavior with a small new-photo increment after catch-up operation is stable.
- Update full-archive delivery status/operating docs to reflect PostgreSQL as the production catalogue.

## Dependency note

M24 operational acceptance occurs after WI-0108 has addressed the slideshow-library/start/playback latency carried forward from M22 acceptance. WI-0108 completed with direct-server and real-phone evidence plus a bounded-prefetch deduplication correction, so WI-0106 is now the remaining substantive M24 work.

## Implementation progress

The first WI-0106 slice establishes the production operations boundary without changing catalogue schema or application semantics:

- `backup-postgres-catalogue.ps1` discovers the Photo Identity database in the running repository PostgreSQL service, requires explicit selection if multiple catalogue databases are present, creates a binary-safe custom-format `pg_dump`, copies it to protected host storage, and writes a SHA-256 report without credentials or connection strings.
- `verify-postgres-backup-restore.ps1` requires explicit application-stop acknowledgement, verifies the backup hash, creates a uniquely named isolated database, restores with `pg_restore --single-transaction`, and compares the current schema version, complete public-table set, exact table row counts and constraint validation state with the stopped production source.
- The isolated restore database is deliberately retained after verification for maintainer inspection; cleanup is an explicit operator action against the exact verification database name.
- `docs/operations/postgresql-operations.md` defines normal startup/shutdown, restart persistence checks, logical backup/restore, PostgreSQL 18 same-major update rules, major-version migration boundaries, failure diagnosis, sustained catch-up evidence and the final daily-style increment gate.

This slice intentionally does not claim the live acceptance criteria before the maintainer runs them against the accepted production catalogue.

## Acceptance criteria
- [ ] Normal operator startup makes PostgreSQL readiness/failure understandable.
- [ ] Persistent catalogue data survives container and PC restart.
- [ ] Backup plus restore into an isolated PostgreSQL database is successfully verified.
- [ ] Full-archive catch-up can run for an extended period without the prior SQLite lock/host-shutdown failure.
- [ ] Progress/failure metrics are sufficient to diagnose stalls without verbose per-photo tracing.
- [ ] A small daily-style increment can be synchronized, analyzed, enriched and reviewed after the catch-up workflow.
- [ ] Maintainer accepts PostgreSQL as the production catalogue and the preserved SQLite rollback snapshot can be retired according to documented policy.
