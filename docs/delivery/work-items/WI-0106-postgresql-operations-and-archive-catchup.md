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

- `backup-postgres-catalogue.ps1` creates a binary-safe custom-format `pg_dump`, copies it to protected host storage, and writes a SHA-256 report without credentials or connection strings. It supports explicit `-DatabaseName` selection when multiple migrated/rehearsal catalogues remain in the same PostgreSQL service.
- The attempted no-argument launcher-derived database selection is not required for acceptance. Live use showed the maintainer's private launcher environment value has a wrapper shape that the generic parser cannot use to recover the database name reliably; the accepted operational path therefore uses explicit database selection rather than guessing or continuing to unwrap private configuration.
- `verify-postgres-backup-restore.ps1` requires explicit application-stop acknowledgement, verifies the backup hash, creates a uniquely named isolated database, restores with `pg_restore --single-transaction`, and compares the current schema version, complete public-table set, exact table row counts and constraint validation state with the stopped production source.
- The isolated restore database is deliberately retained after verification for maintainer inspection; cleanup is an explicit operator action against the exact verification database name.
- `docs/operations/postgresql-operations.md` defines normal startup/shutdown, restart persistence checks, logical backup/restore, PostgreSQL 18 same-major update rules, major-version migration boundaries, failure diagnosis, sustained catch-up evidence and the final daily-style increment gate.

## Live acceptance progress (2026-09-12)

The maintainer completed the first production operations acceptance pass from merged `main`:

- Multiple retained rehearsal databases made schema-marker-only discovery ambiguous. Rather than infer production from private launcher-string formatting, the maintainer compared PostgreSQL database activity before and after real Photo Identity UI use. `photoidentity_rehearsal_20260910_214641_a808bde` showed the dominant activity increase (+1,243 committed transactions, +3,465,666 tuples returned and +414,091 tuples fetched during the observation window), while the other retained rehearsal databases moved only marginally. That database was therefore selected explicitly as the active production authority for the stopped backup.
- With Photo Identity stopped, `backup-postgres-catalogue.ps1 -DatabaseName ...` created `photoidentity-postgresql-20260912-015606.dump` with SHA-256 `9b15dde0bfa4521d62a549850aca8f68c06d9c5db59846c51676e9970c7fdf1f`.
- `verify-postgres-backup-restore.ps1 -ApplicationStopped` restored that exact dump into isolated database `photoidentity_restore_20260911235701_ede110` and passed the schema-version, complete public-table set, exact row-count and validated-constraint comparisons against the stopped source.
- The maintainer reviewed the successful result and removed only the isolated verification database, leaving the production catalogue and verified backup intact.
- With Photo Identity still stopped, the PostgreSQL Compose service was stopped and started again. `verify-postgres.ps1 -SkipContainerStart` succeeded and normal Photo Identity use remained healthy afterward, proving the named-volume catalogue survives container/service restart. An actual Windows/PC restart remains to be observed before the combined restart criterion is marked complete.

The first real catch-up pass then replaced the stale pre-cutover advancement state (`blocked` with `SQLite Error 6: 'database table is locked'.`) with active PostgreSQL-backed synchronization and processing:

- `/health` remained `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready`, schema version 23 throughout the captured run.
- Initial synchronization discovered 304 additional source images, taking `currentImages` from 16,138 to 16,442 and `unverifiedSourceImages` from 408 to 712 before analysis resumed.
- During active catch-up, `analysedImages` advanced from 15,730 to 15,792 (+62), while the post-sync unverified backlog dropped from 712 to 649 (-63). `failedImages` remained 0 and the final snapshot had only one pending image.
- Advancement progressed through `syncing` into `running`, and successive single-job analysis runs completed successfully.
- Managed hydration remained bounded: one transient hydration was observed, then `hydrationsInProgress` returned to 0 with managed hydrated bytes back at 39,055,576 and no managed download in progress at the final snapshot.
- Aggregate diagnostics were sufficient to understand the run without per-photo tracing. By the final snapshot they recorded 64 analysis attempts, analysis-result persistence averaging about 3.94 ms, source hashing averaging about 7.48 ms, bounded per-subject hash-read counts, and one 20.28 s analysis-session initialization cost rather than progressive database degradation.

This closes the diagnostics-observability criterion, but the subsequent longer run exposed a real operational failure that keeps sustained catch-up open:

- PostgreSQL connectivity first failed with a forcibly closed socket and then `127.0.0.1:5432` actively refused new connections.
- `ArchiveAdvancementHostedService` caught its failure and attempted to persist a blocked recovery state; that persistence also failed while PostgreSQL was unavailable, but the archive worker was designed to continue retrying.
- `IdentityMatchRegenerationHostedService` did not have the equivalent outer unexpected-failure boundary. Its repository open failure escaped `ExecuteAsync`, and the default .NET `BackgroundServiceExceptionBehavior=StopHost` stopped the entire Photo Identity API.
- Follow-up host diagnostics then showed the failure was above PostgreSQL: both `podman system connection` endpoints pointed at `127.0.0.1:60828`, that control socket actively refused connections, `podman-machine-default` was stopped, and `wsl --list --verbose` showed the Podman machine plus all other WSL distributions stopped. PostgreSQL therefore became unavailable because the Podman/WSL runtime disappeared underneath it; Compose `restart: unless-stopped` cannot recover a container while the container runtime itself is down.
- After restarting the existing Podman machine, the retained PostgreSQL container was visible as `Exited (0)` with `OOMKilled=false`, `RestartCount=0` and no container error. The retained PostgreSQL log tail contained ordinary checkpoints and no `FATAL`, panic or crash-recovery evidence. This makes an independent PostgreSQL crash unlikely: the database process ended as part of the surrounding Podman/WSL runtime stop. The persisted container and named volume were retained for normal restart/verification rather than recreated.
- The corrective slice adds the same retry-without-host-shutdown behavior already used by other long-lived workers and a regression test proving a transient repository failure does not fault the hosted service. This protects the application from a brief catalogue interruption, but it does not by itself explain why the Podman/WSL machine stopped. The existing Compose service and persisted PostgreSQL catalogue must be restarted and re-verified before catch-up is rerun.

After catch-up is stable, a small real daily-style source increment must still prove synchronization, analysis, enrichment and review without unnecessary full regeneration.

## Acceptance criteria
- [ ] Normal operator startup makes PostgreSQL readiness/failure understandable.
- [ ] Persistent catalogue data survives container and PC restart. (Container/service restart accepted 2026-09-12; PC restart still pending.)
- [x] Backup plus restore into an isolated PostgreSQL database is successfully verified.
- [ ] Full-archive catch-up can run for an extended period without the prior SQLite lock/host-shutdown failure. (Initial PostgreSQL progress was healthy; a later Podman/WSL runtime stop removed PostgreSQL availability and exposed a fatal worker-resilience gap. Corrective retry behavior and runtime recovery are pending retest.)
- [x] Progress/failure metrics are sufficient to diagnose stalls without verbose per-photo tracing.
- [ ] A small daily-style increment can be synchronized, analyzed, enriched and reviewed after the catch-up workflow.
- [ ] Maintainer accepts PostgreSQL as the production catalogue and the preserved SQLite rollback snapshot can be retired according to documented policy.
