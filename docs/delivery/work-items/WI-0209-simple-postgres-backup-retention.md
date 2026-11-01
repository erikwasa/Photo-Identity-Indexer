---
id: WI-0209
title: Keep one durable PostgreSQL recovery backup and retire temporary change backups
milestone: M41
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [backup-postgres-catalogue.ps1, docs]
---

# WI-0209: Keep one durable PostgreSQL recovery backup and retire temporary change backups

## Objective

Avoid accumulating PostgreSQL logical backup files while keeping a dependable way to recover the production catalogue. Keep this deliberately small: a backup is either a **short-lived precaution before a database change** or a **durable recovery checkpoint**. One confirmed recovery backup is sufficient; briefly having two during replacement is acceptable.

## Intended behavior

### 1. Pre-change precaution (local)

- Take a backup before a migration, upgrade, or other potentially disruptive catalogue change, using the existing PostgreSQL backup and verification tooling.
- Keep the backup until the associated change has passed its relevant local/database/application checks. Failed, interrupted or unverified changes must not trigger deletion.
- Once the change is accepted, offer a **single explicit, operator-confirmed cleanup step** for that particular backup and its related metadata. Do not infer successful change acceptance from backup age, a completed `pg_dump`, or a passing isolated restore alone.
- A backup that also serves as the durable recovery checkpoint must not be removed by the temporary-backup cleanup flow.

### 2. Recovery checkpoint (off the primary physical storage)

- Keep **one** current, known-restorable PostgreSQL logical backup independent of the production disk, preferably confirmed uploaded to OneDrive. A local OneDrive-synced folder alone is not evidence of a second physical copy; confirm the remote copy actually exists and is usable.
- Reuse the existing `.dump`, SHA-256 report, and restore-verification workflow. Make it easy to identify which backup is the protected recovery checkpoint.
- When refreshing the checkpoint, create and verify the replacement and confirm its remote availability **before** retiring the previous checkpoint. A short overlap of two backups is expected and safe.
- Never delete the sole confirmed recovery copy, and never treat an unfinished transfer, failed hash comparison, or unverified restore as permission to remove an earlier copy. If the new copy cannot be confirmed, keep the existing checkpoint and report the problem.
- A freshly verified pre-change backup may also become the recovery checkpoint; do not force two separate dumps if one suffices.

## In scope

- Define a simple way to distinguish the two backup purposes and surface the currently protected recovery checkpoint, reusing existing backup manifests and scripts where practical.
- Provide read-only inventory/dry-run and **exact-file**, explicit cleanup for accepted pre-change backups; keep associated `.dump` and report/evidence files coherent.
- Support an operator-driven checkpoint refresh and safe retirement of the superseded copy only after its replacement is checked. If cloud completion cannot be verified programmatically, require an explicit operator confirmation rather than pretending it was verified.
- Keep existing/unclassified backups untouched until individually reviewed. Do not delete arbitrary files or follow links outside approved backup locations.
- Document a short, practical Windows/OneDrive operator workflow and test the failure/protection paths using disposable backups.

## Out of scope

- Automated age/count/size quotas, backup rotation schedules, or a backup service/daemon.
- Automatically deleting backups just because verification passed, or claiming a database change is safe without post-change checks.
- Building a OneDrive cloud API integration or assuming a sync-client status proves off-device durability.
- PostgreSQL production data cleanup, temporary restored-database cleanup (WI-0208), and Podman/WSL VHDX compaction.
- Enabling unattended deletion of existing or unknown backup files.

## Acceptance criteria

- [ ] Operators can clearly identify a short-lived pre-change backup and the single protected recovery checkpoint, without new scheduling or retention machinery.
- [ ] After successful **change** verification, an explicit bounded action removes the selected temporary backup and its related sidecars; failure or missing acceptance leaves it intact.
- [ ] Exactly one confirmed, restore-capable recovery checkpoint can be retained off the production disk (preferably on OneDrive), with documented confirmation of upload/remote availability and matching integrity evidence.
- [ ] Replacing that checkpoint can temporarily keep two backups; the old checkpoint is not retired until the new one is verified and independently available. Failed refresh does not lose the old recovery point.
- [ ] Tests prove protection of the only recovery copy, ambiguous/unclassified paths, incomplete transfers, bad hashes, missing verification evidence and cleanup failure.
- [ ] Windows operator documentation remains concise; `PhotoIdentity.Docs validate` and `generate --check` pass, followed by maintainer verification of the practical workflow.

## Relationship to WI-0208

[WI-0208](WI-0208-postgres-restore-storage-lifecycle.md) manages temporary **PostgreSQL databases** created by isolated restore checks, not `.dump` backups. It may dispose of a successfully verified temporary database while leaving all backup files intact. WI-0209 is a separate workitem with **no prerequisite dependency** for WI-0208.

## Current status

**In progress — first implementation slice (2026-11-01).** WI-0208 is completed. This PR adds an operator-driven local backup inventory and exact-file manager, protected checkpoint registration and explicit retention flows, plus isolated synthetic refusal tests and a Windows runbook. It intentionally does **not** implement a background daemon, automatic backup deletion, OneDrive API or cloud transfers.

**Implemented in this slice:** an approved-root local state file distinguishes unclassified, registered pre-change, current protected recovery and superseded checkpoints. Exact backups are pinned to their SHA-256 manifest, source, format and byte length. Current checkpoint promotion additionally requires a passing isolated restore result with completed temporary database cleanup and explicit operator-supplied evidence of a separately available remote copy with matching SHA-256. During replacement the old checkpoint is retained as superseded until a separate, exact-file confirmed retirement; its replacement is revalidated before deletion. Pre-change cleanup requires registration, explicit confirmation that the *associated change* passed and a separate explicit deletion switch. Missing hashes, restore evidence, remote confirmation, ambiguous names, path escapes or reparse files fail closed. Original unclassified dumps remain untouched.

**Verification planned for this PR:** Windows CI parser and synthetic/disposable-file tests exercise role registration, unclassified/sole-checkpoint protection, failed restore and cloud evidence, tampering, missing metadata, superseded-transition protection and exact-file cleanup. Run `pwsh -NoProfile -File .\tests\PowerShell\postgres-backup-retention.tests.ps1`; validate docs and generated status. This is not evidence of a real off-disk recovery copy.

**Post-merge maintainer checkpoint, 2026-11-01:** PR #701 merged with successful CI. The maintainer ran the disposable local PowerShell suite: **44 backup-retention assertions passed**, `PhotoIdentity.Docs validate` passed and `generate --check` completed without drift. Read-only inventory enumerated **six existing `unclassified` dumps** (each about 2.25 GiB) with manifest and restore-verification sidecars present; inventory alone does not validate the sidecar contents or prove an independent recovery copy. No existing backup was classified or deleted. The WI-0208 accepted full-restore backup is the leading candidate for reuse as the durable checkpoint, without another full-size restore.

**Follow-up hardening:** add a true read-only `-InspectBackup` for an existing unclassified candidate, verifying dump hash/manifest and whether its restore-report comparison and automatic cleanup passed. Keep remote independence strictly separate: a recorded remote attestation is not a live OneDrive check. Correct the partial-deletion order so a Windows-locked dump refuses deletion **without discarding its manifest and restore evidence first**, with isolated regression coverage. This follow-up is still pending CI and must not be interpreted as local off-disk checkpoint verification.

**Still required before acceptance:** test the operator workflow locally with a small disposable backup and actual post-change approval; select one real restore-verified recovery dump and independently confirm the OneDrive/off-disk copy (and matching hash) before promotion. Verify old-checkpoint replacement while preserving at least one confirmed recovery point, refusal paths in the maintained environment, and successful exact cleanup. No deletion of the user's existing unclassified archive backups should happen during development.
