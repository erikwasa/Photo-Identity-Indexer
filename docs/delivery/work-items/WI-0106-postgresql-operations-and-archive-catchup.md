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

M24 operational acceptance occurs after WI-0108 has addressed the slideshow-library/start/playback latency carried forward from M22 acceptance. PostgreSQL operations are not considered fully accepted while those known scale-path delays remain unresolved.

## Acceptance criteria
- [ ] Normal operator startup makes PostgreSQL readiness/failure understandable.
- [ ] Persistent catalogue data survives container and PC restart.
- [ ] Backup plus restore into an isolated PostgreSQL database is successfully verified.
- [ ] Full-archive catch-up can run for an extended period without the prior SQLite lock/host-shutdown failure.
- [ ] Progress/failure metrics are sufficient to diagnose stalls without verbose per-photo tracing.
- [ ] A small daily-style increment can be synchronized, analyzed, enriched and reviewed after the catch-up workflow.
- [ ] Maintainer accepts PostgreSQL as the production catalogue and the preserved SQLite rollback snapshot can be retired according to documented policy.
