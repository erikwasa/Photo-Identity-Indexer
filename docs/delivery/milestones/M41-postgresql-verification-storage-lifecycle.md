---
id: M41
title: PostgreSQL backup and verification storage lifecycle
status_source: ../status/milestones.yaml
depends_on: []
---

# M41: PostgreSQL backup and verification storage lifecycle

## Outcome

Keep PostgreSQL backup/restore verification trustworthy without indefinitely accumulating temporary restored databases or obsolete local pre-change backup files. Preserve one confirmed, off-disk recovery checkpoint without introducing a general-purpose backup scheduler or rotation system.

## User-visible demonstration

A passing representative isolated restore produces a durable, privacy-safe comparison report and automatically disposes only its own disposable restored database; explicit diagnostic retention and failure/resume remain available. The maintainer can inspect stale copies, dry-run cleanup and see the actual volume/disk space before and after optional offline VHDX compaction. Separately, a verified change permits explicit cleanup of its local pre-change backup, while one known-restorable recovery backup remains independently available, preferably confirmed on OneDrive.

## Work items

- [WI-0208](../work-items/WI-0208-postgres-restore-storage-lifecycle.md) — automatic successful-restore **database** cleanup, safe failure/retention/recovery controls, inventory and verification.
- [WI-0209](../work-items/WI-0209-simple-postgres-backup-retention.md) — simple, explicit cleanup of accepted **pre-change backup files** while protecting one confirmed off-disk recovery checkpoint; independent of WI-0208.

## Exit criteria

- [x] Default successful verification no longer accumulates isolated restore databases and accurately reports cleanup (WI-0208 maintained Windows acceptance, 2026-11-01).
- [x] Failure diagnostics, explicit retention and resume remain safe and auditable (WI-0208 synthetic and disposable integration verification).
- [x] Tests and maintained-host verification establish that production data and backups are preserved for the restore-copy lifecycle (WI-0208).
- [x] Runbook explains safe cleanup and Windows VM-disk reclamation separately.
- [ ] A single confirmed recovery checkpoint survives independently of the production disk, and accepted pre-change backup files can be retired without age/count rotation.

## Risks

A misidentified database can destroy production data; cleanup must use exact generated identity, proven ownership and source exclusion. Cleanup before report persistence can lose restore evidence. WSL VHDX compaction while mounted can cause corruption. Retaining failure diagnostics indefinitely can also regrow storage, so stale inventory and operator-confirmed disposal must be available. Deleting an old checkpoint before confirming the replacement has reached independent storage could leave no recovery path.

## Evidence

2026-10-31 maintainer diagnostic: four approximately 13 GB isolated restores accumulated; three older copies were manually removed and Windows free storage increased to 128.13 GiB after separate, offline Podman VHDX compaction.

2026-11-01 **WI-0208 completed and accepted**: root cause of full-sized cleanup refusal diagnosed as PostgreSQL autovacuum workers; a bounded autovacuum-only grace period now permits ordinary, ownership-checked automatic removal after maintenance ends. The maintainer passed 59 source-free safety assertions, 10 synthetic full-verifier scenarios, and an actual restore of the approximately 17.86 GiB production catalogue. The new restore database `photoidentity_restore_20261101132922_afb037` was removed automatically (`passed=true`, `cleanupStatus=removed`, `cleanupRequired=false`); independent inventory showed zero copies, and backup plus hash manifest survived. Older unmarked legacy restore databases are protected. The acceptance transcript does not provide paired Podman-volume before/after figures; no Windows VHDX compaction benefit is inferred. PRs #688, #693, #696, #698 and #699 delivered the lifecycle and safe diagnostic/cleanup improvements.

**Milestone M41 remains open** because WI-0209 (retiring accepted pre-change backup files while preserving a confirmed independent recovery checkpoint) is not completed. Do not conflate verified restore-copy removal with backup-file retention.
