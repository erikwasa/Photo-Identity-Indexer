---
id: M41
title: PostgreSQL verification storage lifecycle
status_source: ../status/milestones.yaml
depends_on: []
---

# M41: PostgreSQL verification storage lifecycle

## Outcome

Keep PostgreSQL backup/restore verification trustworthy without indefinitely accumulating full-size temporary restored catalogues on the operator's Windows/Podman machine.

## User-visible demonstration

A passing representative isolated restore produces a durable, privacy-safe comparison report and automatically disposes only its own disposable restored database; explicit diagnostic retention and failure/resume remain available. The maintainer can inspect stale copies, dry-run cleanup and see the actual volume/disk space before and after optional offline VHDX compaction.

## Work items

- [WI-0208](../work-items/WI-0208-postgres-restore-storage-lifecycle.md) — automatic successful-restore cleanup, safe failure/retention/recovery controls, inventory and verification.

## Exit criteria

- [ ] Default successful verification no longer accumulates isolated restore databases and accurately reports cleanup.
- [ ] Failure diagnostics, explicit retention and resume remain safe and auditable.
- [ ] Tests and maintained-host verification establish that production data and backups are preserved.
- [ ] Runbook explains safe cleanup and Windows VM-disk reclamation separately.

## Risks

A misidentified database can destroy production data; cleanup must use exact generated identity, proven ownership and source exclusion. Cleanup before report persistence can lose restore evidence. WSL VHDX compaction while mounted can cause corruption. Retaining failure diagnostics indefinitely can also regrow storage, so stale inventory and operator-confirmed disposal must be available.

## Evidence

2026-10-31 maintainer diagnostic: four approximately 13 GB isolated restores accumulated; three older copies were manually removed and Windows free storage increased to 128.13 GiB after Podman VHDX compaction. No automated lifecycle fix has yet been implemented or verified.
