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

- [x] Operators can clearly identify short-lived pre-change backups and the single protected recovery checkpoint, without scheduling or retention machinery. The current checkpoint is registered and independently inspected.
- [x] Exact pre-change retirement requires explicit post-change acceptance and deletion confirmation; Windows disposable-file tests exercised successful removal of dump and sidecars and refusal without acceptance. **No real pre-change backup was deleted.**
- [x] Exactly one restore-verified recovery checkpoint is registered and independently available on OneDrive, with upload, separate browser download and matching SHA-256 verified by the maintainer on 2026-11-01.
- [x] Windows disposable-file tests exercised simultaneous original/replacement checkpoints, refusal to retire the old copy when the replacement manifest was missing, and separate confirmed retirement only after replacement evidence was restored. No second real recovery copy was required.
- [x] Source-free disposable tests prove protection of the only recovery copy, ambiguous/unclassified paths, incomplete transfers, bad hashes, missing verification evidence and cleanup refusal (49 local assertions passed after PR #702).
- [x] Windows operator runbook is maintained; the maintainer reported passing `PhotoIdentity.Docs validate` and `generate --check`, 49 source-free Windows assertions, and independent real OneDrive download/verification and checkpoint registration.

## Relationship to WI-0208

[WI-0208](WI-0208-postgres-restore-storage-lifecycle.md) manages temporary **PostgreSQL databases** created by isolated restore checks, not `.dump` backups. It may dispose of a successfully verified temporary database while leaving all backup files intact. WI-0209 is a separate workitem with **no prerequisite dependency** for WI-0208.

## Current status

**Initial implementation phase, now completed (2026-11-01).** WI-0208 is completed. This PR adds an operator-driven local backup inventory and exact-file manager, protected checkpoint registration and explicit retention flows, plus isolated synthetic refusal tests and a Windows runbook. It intentionally does **not** implement a background daemon, automatic backup deletion, OneDrive API or cloud transfers.

**Implemented in this slice:** an approved-root local state file distinguishes unclassified, registered pre-change, current protected recovery and superseded checkpoints. Exact backups are pinned to their SHA-256 manifest, source, format and byte length. Current checkpoint promotion additionally requires a passing isolated restore result with completed temporary database cleanup and explicit operator-supplied evidence of a separately available remote copy with matching SHA-256. During replacement the old checkpoint is retained as superseded until a separate, exact-file confirmed retirement; its replacement is revalidated before deletion. Pre-change cleanup requires registration, explicit confirmation that the *associated change* passed and a separate explicit deletion switch. Missing hashes, restore evidence, remote confirmation, ambiguous names, path escapes or reparse files fail closed. Original unclassified dumps remain untouched.

**Initial verification plan (subsequently passed):** Windows CI parser and synthetic/disposable-file tests exercise role registration, unclassified/sole-checkpoint protection, failed restore and cloud evidence, tampering, missing metadata, superseded-transition protection and exact-file cleanup. Run `pwsh -NoProfile -File .\tests\PowerShell\postgres-backup-retention.tests.ps1`; validate docs and generated status. This is not evidence of a real off-disk recovery copy.

**Post-merge maintainer checkpoint, 2026-11-01:** PR #701 merged with successful CI. The maintainer ran the disposable local PowerShell suite: **44 backup-retention assertions passed**, `PhotoIdentity.Docs validate` passed and `generate --check` completed without drift. Read-only inventory enumerated **six existing `unclassified` dumps** (each about 2.25 GiB) with manifest and restore-verification sidecars present; inventory alone does not validate the sidecar contents or prove an independent recovery copy. No existing backup was classified or deleted. The WI-0208 accepted full-restore backup is the leading candidate for reuse as the durable checkpoint, without another full-size restore.

**Follow-up hardening delivered by merged PR #702:** added a true read-only `-InspectBackup` for an existing unclassified candidate, verifying dump hash/manifest and whether its restore-report comparison and automatic cleanup passed. Keep remote independence strictly separate: a recorded remote attestation is not a live OneDrive check. Corrected the partial-deletion order so a Windows-locked dump refuses deletion **without discarding its manifest and restore evidence first**, with isolated regression coverage. This implementation alone did not prove an off-disk recovery copy; the subsequent independent upload/download and hash comparison did.

**Maintainer confirmation of independently usable recovery checkpoint — 2026-11-01:** PR #702 was merged. The maintainer ran the updated disposable test suite with **49 assertions passed**. Read-only inspection of the previously accepted `wi0208-acceptance-20261101-142600.dump` confirmed: source `photoidentity_main`, exact 2,398,115,284-byte dump, SHA-256 `5d743b0821b6bdcbc72ee5c6e2d749ffb592ef5f01331ab2566efb83a163f759`, matching size/hash manifest, passing full-size restore report and `cleanupStatus=removed`. It initially had no remote confirmation.

The maintainer then **uploaded that dump to OneDrive, retrieved it again into a separate local folder, independently SHA-256-hashed the retrieved bytes, and confirmed the download matched the original fingerprint**. The `-SetRecoveryCheckpoint -ExpectedSha256 ... -RemoteSha256 ... -RemoteConfirmation ... -ConfirmIndependentCopy` operation succeeded. Subsequent `-InspectBackup` returned `Role=recovery-checkpoint`, `EligibleForCheckpointPromotion=True` and `RemoteConfirmationRecorded=True`. This is an **operator-confirmed independent copy**, not an automated OneDrive API health check; future remote durability should not be assumed without rechecking. No additional full-size PostgreSQL restore was required and no other existing backups were deleted or reclassified.

**Final acceptance and closeout (2026-11-01):** WI-0209 is **completed**. The maintainer already executed the 49-assertion source-free Windows suite, which uses a fresh randomly named temporary backup root and invokes the actual operator management script, not mocked command outcomes. The suite exercises registration of a pre-change precaution, a read-only exact-file removal plan, refusal before explicit change acceptance, a real deletion of that synthetic dump and its two sidecars after explicit acceptance, refusal to delete the current checkpoint, two-copy replacement with old copy held as superseded, refusal when the replacement manifest is missing, and removal of the superseded copy only after the replacement is validated. It additionally covers a Windows-locked dump, failed/missing restore evidence, hash tampering, missing remote confirmation, ambiguous files and concurrent registry locking. Repeating the same tests in a second temporary directory would not add a materially different acceptance boundary. This evidence **does not claim that a real production schema change was performed or that an existing real backup was deleted**.

The independently uploaded and downloaded OneDrive copy has a matching SHA-256 and the previously full-size restore-verified backup has been registered as the sole protected recovery checkpoint (details above). The other five previously inventoried backups remain **unclassified and untouched**, pending individual operator review; they are not automatically eligible for retirement. No unattended rotation, remote health monitoring, new full restore, PostgreSQL database changes, OneDrive object deletion or Windows VHDX compaction is part of this work item.

**Milestone M41 exit:** with both WI-0208 and WI-0209 accepted, M41 can be marked completed. Any future refresh of the off-disk recovery checkpoint is a separate operator maintenance decision following the same verification and overlap procedure.
