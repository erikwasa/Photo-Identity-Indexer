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
- Managed hydration remained bounded and aggregate diagnostics were sufficient to explain stage costs without per-photo tracing.

A later longer run exposed a Podman/WSL runtime stop and a fatal worker-resilience gap. Diagnostics showed the Podman machine and WSL distributions stopped while the retained PostgreSQL container itself had exited cleanly (`ExitCode=0`, `OOMKilled=false`, `RestartCount=0`) with no PostgreSQL crash evidence. PR #309 hardened `IdentityMatchRegenerationHostedService` so a transient catalogue interruption is logged and retried instead of escaping `BackgroundService` and stopping the API.

After PR #309 merged, the maintainer restarted the existing Podman machine and Compose service, reran `verify-postgres.ps1`, started Photo Identity normally, and performed a fresh sustained catch-up acceptance run from reset generation 1:

- Baseline at 17:57 UTC showed `currentImages=16,442`, `analysedImages=15,990`, `unverifiedSourceImages=452`, `pendingImages=0`, `failedImages=0`; PostgreSQL was `ready` at schema 23 and the Podman machine/Compose service were running.
- At 18:16 local time the archive had advanced to `analysedImages=16,164` and `unverifiedSourceImages=277` with `failedImages=0` and PostgreSQL still `ready`.
- At the final 18:53 checkpoint, all 16,442 current images were analysed: `analysedImages=16,442`, `unverifiedSourceImages=0`, `pendingImages=0`, `failedImages=0`. The latest single-job analysis run completed successfully and PostgreSQL remained `ready`.
- The approximately 57-minute run recorded 452 analysis attempts, matching the 452-image baseline backlog. Analysis-result persistence averaged about 5.21 ms and analysis source hashing about 10.03 ms. Analysis, original-open and source-verification hash reads were each exactly one per subject; original-status reads stayed bounded at two per subject.
- Hydration did not accumulate: the final snapshot had `hydrationsInProgress=0`, `managedDownloadingBytes=0`, and managed hydrated bytes returned to 39,055,576. The final advancement state was `waiting` only for a OneDrive-managed download/release transition after the analysis backlog had already reached zero.

This accepts the sustained full-archive catch-up criterion.

The maintainer then exercised a real daily-style increment from reset diagnostics generation 2 without full regeneration:

- Baseline was the fully caught-up catalogue: `currentImages=16,442`, `analysedImages=16,442`, `unverifiedSourceImages=0`, `pendingImages=0`, `failedImages=0`, with advancement explicitly paused and PostgreSQL `ready` at schema 23.
- Seven newly synced source images were discovered. Final totals were `currentImages=16,449` and `analysedImages=16,449`, with `unverifiedSourceImages=0`, `pendingImages=0`, `failedImages=0`.
- The latest analysis run contained exactly seven jobs and completed with seven succeeded, zero failed and zero cancelled jobs. Aggregate diagnostics also recorded exactly seven analysis attempts, demonstrating that the already-complete 16,442-image catalogue was not unnecessarily reprocessed.
- Analysis result persistence averaged about 3.57 ms and source hashing about 1.50 ms. Analysis/original-open/synchronization hash reads were each one per new subject; original-status stayed bounded at two reads per subject.
- Six face-review derivative revisions were generated, hydration ended with zero in progress and zero managed downloading bytes, and PostgreSQL remained `ready`.

This is sufficient evidence for the synchronization/analysis/no-full-regeneration portion of the daily-style increment gate. Before marking the whole criterion complete, retain one explicit confirmation that the new photos are visible through the normal Review experience and capture the place-enrichment counters (a zero-candidate result is acceptable when the seven photos contain no GPS metadata).

An actual Windows/PC restart and final production-authority/SQLite-retirement decision also remain before WI-0106/M24 closeout.

## Acceptance criteria
- [ ] Normal operator startup makes PostgreSQL readiness/failure understandable.
- [ ] Persistent catalogue data survives container and PC restart. (Container/service restart accepted 2026-09-12; PC restart still pending.)
- [x] Backup plus restore into an isolated PostgreSQL database is successfully verified.
- [x] Full-archive catch-up can run for an extended period without the prior SQLite lock/host-shutdown failure. (Accepted 2026-09-12 after PR #309: the 452-image remaining backlog reached zero over an approximately 57-minute run with PostgreSQL ready and zero failed images.)
- [x] Progress/failure metrics are sufficient to diagnose stalls without verbose per-photo tracing.
- [ ] A small daily-style increment can be synchronized, analyzed, enriched and reviewed after the catch-up workflow. (Seven-photo sync/analysis/no-regeneration processing accepted; explicit Review visibility and enrichment-counter confirmation still pending.)
- [ ] Maintainer accepts PostgreSQL as the production catalogue and the preserved SQLite rollback snapshot can be retired according to documented policy.
