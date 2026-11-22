---
id: WI-0208
title: Bound PostgreSQL restore-verification storage and cleanup
milestone: M41
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [verify-postgres-backup-restore.ps1, PhotoIdentity.Docs, docs]
---

# WI-0208: Bound PostgreSQL restore-verification storage and cleanup

## Objective

Prevent repeated PostgreSQL backup/restore verification from leaving large, indefinitely retained temporary databases in the production Podman cluster, without compromising the safety, observability, or resumability of restore verification.

## Why

**Maintainer observation (2026-10-31):** the maintained Windows/PostgreSQL 18 environment accumulated four `photoidentity_restore_*` verification databases of approximately 13 GB each alongside an approximately 18 GB production database. Three older restore copies were identified as obsolete and manually dropped; the production database was not modified. The Podman WSL VHDX remained large until offline compaction. Free Windows storage increased from about 77 GiB before compaction to 128.13 GiB afterwards. This is a local operational observation, not automated test evidence or proof that every reclaimed GiB came from these databases.

The existing verifier `verify-postgres-backup-restore.ps1` intentionally retains an isolated restored database **after both success and failure**, setting `cleanupRequired = true` in its JSON report. The operations runbook requires subsequent manual deletion. Repeated verification runs therefore accumulate full-size catalogue copies unless the maintainer remembers to perform cleanup. This item revises that deliberately documented behavior; it is not an unexplained PostgreSQL leak or a change to the production catalogue.

## In scope

- Map the verifier's create/restore/compare/report/resume lifecycle, including exceptions, interruption, container-copy cleanup and `-ResumeRestoredDatabase`.
- After **successful verification and durable report publication**, default to removing only the temporary database created by that verifier invocation. Keep independent backup `.dump`, backup hash/JSON report, sanitized comparison evidence, and a non-ambiguous cleanup outcome in the verification report.
- Provide an explicit documented **retain-for-inspection** option for a successful verification, and preserve the ability to resume or diagnose a failed/partial verification. Do not silently destroy failure evidence before an operator has the required diagnostics.
- Make cleanup failures observable and actionable: distinguish verified restore from completed cleanup; include the generated database name and safe manual recovery procedure without credentials, original paths, or catalogue contents in committed evidence. Avoid reporting success with hidden retained storage.
- Provide a read-only inventory/dry-run of stale verification databases, with bounded, exact-name, operator-confirmed removal. Account for orphaned successful restores, failed restores and interrupted runs, and clearly distinguish rehearsal/test databases from production or operator-retained clones.
- Enforce hard safety boundaries: generated `photoidentity_restore_YYYYMMDDHHMMSS_xxxxxx` names, explicit provenance/ownership validation, refusal to drop the source/current application catalogue, no wildcard deletion, no terminating production connections, and fail-closed behavior on ambiguous identity. Never use timestamps or prefix matching alone as proof that deletion is safe.
- Update the PostgreSQL operations runbook and any affected schema/video rehearsal procedures to describe success cleanup, explicit retention, failure recovery, and Windows Podman/WSL VHDX compaction as a separate **optional, offline** operation after verified backup and graceful shutdown.
- Add tests for success, comparison failure, restore failure, report-write failure, cleanup failure, cancellation/interruption, resume, explicit retention and production-name protection. Exercise the real Podman/PostgreSQL boundary at an appropriate disposable test layer.
- Consider disk-capacity preflight/warnings before creating another full-size restore and record enough sanitized storage metrics to expose growing temporary verification storage without retaining private data.

## Out of scope

- Vacuuming, truncating, changing schema, or deleting tables/indexes in the actual `photoidentity_main` production database.
- Removing, rotating, or changing retention of PostgreSQL logical backup `.dump` files. [WI-0209](WI-0209-simple-postgres-backup-retention.md) separately owns short-lived pre-change backups and the single durable recovery checkpoint; successful cleanup here must **not** delete backup files or their integrity/verification evidence.
- Removing unrelated development databases, Podman volumes, OneDrive files, source originals, or photo/video derivatives.
- Automatically compacting a running Podman/WSL VHDX, replacing the production volume, or claiming disk-space recovery without measuring Windows free space.
- Changing the backup's verified restore integrity requirements or treating a successful restore alone as migration approval.

## Acceptance criteria

- [x] A passing fresh isolated restore leaves **no unintended verification database** after report persistence, with backup/hash/comparison evidence retained and cleanup reported accurately.
- [x] A documented explicit retain option preserves one restorable inspection copy, while default behavior no longer accumulates a copy on every passing verification.
- [x] A failed or interrupted restore remains diagnosable and can be resumed or safely retired; no cleanup failure is hidden and no second orphan is created by an unsafe retry.
- [x] No execution path can drop production or an arbitrary non-verifier database; an ambiguous name, current catalogue, live session, ownership mismatch or missing provenance fails closed.
- [x] Read-only inventory and operator-confirmed cleanup safely identify and retire old verified restore copies, with a dry run and no wildcard/mass-prune behavior.
- [x] Disposable PostgreSQL integration covers success/failure/resume/retention/protection, and the maintained Windows test independently confirms full-size restore verification, database-copy absence and retained backup/hash evidence. Earlier baseline PostgreSQL/Podman volume observations are recorded in the work-item history; **paired Podman-volume before/after figures were not included in the final acceptance transcript** (nonblocking observability limitation, not proof of Windows VHDX reclamation).
- [x] Operational documentation distinguishes reclaimed space **inside PostgreSQL** from Windows VHDX compaction, verifies backup availability and database health, and does not recommend pruning production volumes.
- [x] `PhotoIdentity.Docs validate` and `generate --check` pass, and the maintainer confirms a representative verified-backup run with no accumulating restore database.

## Verification and safety

Use disposable PostgreSQL test databases and non-personal fixtures for automated destructive-path tests. On a maintained system, confirm active production database from the application/connection, maintain a verified logical backup, record the intended generated restore name, and require explicit maintainer authorization before retiring previously retained copies. Collect only aggregate storage/count/status evidence in the repository; never commit database dumps, identities, photos, connection strings, private local paths, or full table contents.

References: [PostgreSQL operations](../../operations/postgresql-operations.md), [WI-0208 Windows acceptance commands](../../operations/postgres-wi0208-verification.md), [WI-0106](WI-0106-postgresql-operations-and-archive-catchup.md), and `verify-postgres-backup-restore.ps1`.

## Separation from backup retention

This workitem only disposes of isolated PostgreSQL **restore-verification databases**. Even after a successful restore check, the input backup and its SHA-256/report remain untouched. [WI-0209](WI-0209-simple-postgres-backup-retention.md) addresses the maintainer's distinct local pre-change and off-disk recovery-backup needs without blocking WI-0208.

## Current status

**Completed — maintainer-accepted on 2026-11-01.** PRs #688, #693, #696, #698 and #699 delivered ownership-fenced default cleanup, a short retry, sanitized per-check diagnostics and a bounded autovacuum-only grace period. CI for PR #699 passed. The maintained Windows/PostgreSQL 18 final full-size acceptance run passed both database-integrity verification and automatic deletion of the exact, owned restored database **without terminating sessions or dropping production data**. The backup and SHA-256 manifest were preserved. This resolves the core storage-accumulation problem for successful restore verification.

**Final acceptance evidence (2026-11-01, local maintainer):**

- Source-free lifecycle safety/report tests: **59 assertions passed**; fake-Podman full-verifier boundary tests: **10 scenarios passed**. Earlier disposable real-Podman/PostgreSQL integration fixture had passed success, failure-preservation, resume, retention and protected disposal.
- Photo Identity API was verified stopped. Fresh backup of the explicitly selected `photoidentity_main` was produced: `wi0208-acceptance-20261101-142600.dump`, SHA-256 `5d743b0821b6bdcbc72ee5c6e2d749ffb592ef5f01331ab2566efb83a163f759`. The verifier reported an approximate **17.86 GiB** source size, validated the complete isolated restoration and then observed autovacuum workers during cleanup.
- The verifier entered the bounded maintenance-aware cleanup phase and **automatically removed** its exact generated restored database `photoidentity_restore_20261101132922_afb037`. Final report: `passed=true`, `cleanupStatus=removed`, `cleanupRequired=false`; independent inventory found **zero remaining copies of that database**. Backup `.dump` and hash/JSON manifest both still existed.
- The older **12.79 GiB unmarked legacy** restore `photoidentity_restore_20261031173251_0d295e` remained inventoried with zero sessions and `unmarked-legacy-do-not-auto-remove`; it was **not** deleted. Prior failed-cleanup reports remain historically accurate, and their safely retired owned copies are not recreated.
- **Evidence limit:** the submitted final acceptance transcript does not include paired `podman system df`/volume-usage before/after figures. Exact owned-database absence and backup preservation *were* verified. PostgreSQL reclaimed database space is not the same as reclaimed Windows VHDX capacity; no compaction or production-volume pruning was performed as part of this acceptance.

**Scope boundary:** WI-0208 is closed for restore-verification **database** lifecycle. Backup-file retention and independent recovery-checkpoint retirement are separate **WI-0209** work; milestone M41 remains open.

### Implementation and diagnostic history

**Initial implementation checkpoint (2026-10-31; subsequently completed).** The verifier now creates an ownership-marked isolated database, preserves failures and read-only resumes, commits a passing comparison report before default cleanup, records the exact cleanup outcome, and leaves a copy only with explicit retention. A separate read-only inventory and exact-name, marker-validated disposal tool refuses unmarked legacy copies, live sessions, arbitrary names and production source matches. Schema rehearsal runbooks explicitly request retention. A PowerShell source-free regression suite is integrated into Windows CI; an opt-in isolated real-Podman/real-PostgreSQL fixture is available but has not been run; maintained Windows verification remains outstanding.

**Maintained-host evidence, 2026-11-01:** PR #688 merged with successful CI. The maintainer confirmed nine source-free verifier scenarios, documentation validate/generate checks, and the real isolated disposable PostgreSQL fixture's success, retention/resume, duplicate prevention, comparison-failure preservation and exact-name disposal paths. In the first 17.86-GiB-source full-sized test the complete restore comparison passed and its copy was 12.83 GiB, but automatic cleanup safely refused a database with an observed active session. Subsequent inventory showed the exact copy owned with zero sessions; the maintainer used the marker-validated, confirmed operator command and verified it was absent afterwards. The legacy unmarked copy was not touched. This demonstrates correct failure reporting and manual recovery, **not** successful automatic cleanup of the full-sized copy.

**Follow-up implementation:** add a bounded (up to 20 seconds) automatic post-restore wait with rechecked exact ownership, zero-session observation before a single non-force drop, and no client termination. Persistent connections, changed provenance or missing databases must still fail closed. The manual operator removal command remains immediate. Add deterministic source-free regression cases for transient/persistent sessions and changed ownership plus a synthetic full-verifier scenario.

**Maintained-host follow-up, 2026-11-01:** The bounded session-wait change from PR #693 passed 32 source-free safety assertions and 10 synthetic full-verifier scenarios locally. On the second maintained 17.86 GiB source restore, comparison again passed but all 11 automatic cleanup checks saw a session; the retained exact-owned copy was `photoidentity_restore_20261101011308_a6be9c`. Backend type was not captured by the verifier. This is **not** acceptance of automatic cleanup. A diagnostics-only follow-up adds sanitized backend-type/count evidence on cleanup failure and a read-only operator query so the blocker can be identified without another full restore.

**Maintained-host recovery and diagnostics, 2026-11-01:** The second owned, 12.83 GiB restore was subsequently inventoried with zero sessions and manually removed by exact generated name and matching ownership marker, with absence confirmed. The maintainer's post-PR #696 regression suites passed **38 safety/report assertions** and **10 synthetic verifier scenarios**. Neither manual recovery nor the diagnostic regression tests prove automatic cleanup in the maintained system. Backend type during the original two busy windows remains unknown.

**Consolidated next diagnostic:** instrument the existing 11 automatic-cleanup observations to record category-only backend type, recognized client category, state and `pg_stat_progress_analyze`/`pg_stat_progress_vacuum` aggregate counts in the durable verification report. Collect this evidence **during one future maintained full-size restore**, whether cleanup succeeds or fails, instead of relying on a later `pg_stat_activity` query after the sessions have disappeared. Differentiate PostgreSQL autovacuum maintenance, a lingering `psql`/`pg_restore` client, unrelated application-client sessions, and other PostgreSQL workers. The instrumentation is read-only, best-effort and does not change ownership checks, retries, refusal behavior or backup retention.

**Root cause established, 2026-11-01:** The maintainer ran the merged consolidated diagnostic capture on a fresh 17.86 GiB source. Source/restore schema, table set and exact counts passed, but automatic cleanup failed after 11 observations. **Every** recorded blocked observation identified **only `autovacuum worker`** (one or two active sessions), with `pg_stat_progress_vacuum` active on every observation and `pg_stat_progress_analyze` active on one. No `client backend` appeared. The exact new owned restored copy `photoidentity_restore_20261101114757_407d0a` was approximately 12.83 GiB, later observed with **zero sessions**, and remains to be retired by the protected exact-marker command. Thus this is a maintenance-timing interaction, not an observed leaked application connection; do not infer autovacuum was present in earlier uncaptured tests beyond the supporting similarity of their behavior.

**Targeted implementation:** Extend the existing 11-check short wait **only while classified exclusively as autovacuum workers**, bounded to at most 60 more checks separated by 10 seconds. Recheck the exact ownership marker, generated name and zero sessions before any ordinary `dropdb`. A client, mixed/unknown type, missing diagnostic, provenance mismatch, or maintenance lasting beyond the bound must fail closed and retain the copy. No global autovacuum change, backend termination, `DROP DATABASE FORCE`, backup deletion, volume operation, or change to explicit retained rehearsals is authorized. Source-free safety tests must cover both completion after the ordinary window and each refusal boundary.

**Former acceptance gate — satisfied 2026-11-01:** The final maintained Windows full-size run passed and the restored copy was automatically removed with the backup and manifest preserved (see final acceptance evidence above). Prior reports showing `cleanupStatus=failed` remain truthful. No destructive maintenance on unmarked legacy databases or Windows VHDX is authorized.
