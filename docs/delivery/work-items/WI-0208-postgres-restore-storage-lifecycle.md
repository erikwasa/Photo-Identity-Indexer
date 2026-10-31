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

- [ ] A passing fresh isolated restore leaves **no unintended verification database** after report persistence, with backup/hash/comparison evidence retained and cleanup reported accurately.
- [ ] A documented explicit retain option preserves one restorable inspection copy, while default behavior no longer accumulates a copy on every passing verification.
- [ ] A failed or interrupted restore remains diagnosable and can be resumed or safely retired; no cleanup failure is hidden and no second orphan is created by an unsafe retry.
- [ ] No execution path can drop production or an arbitrary non-verifier database; an ambiguous name, current catalogue, live session, ownership mismatch or missing provenance fails closed.
- [ ] Read-only inventory and operator-confirmed cleanup safely identify and retire old verified restore copies, with a dry run and no wildcard/mass-prune behavior.
- [ ] Disposable PostgreSQL integration tests cover success/failure/resume/retention/protection, and a maintained Windows test records before/after PostgreSQL database sizes and Podman volume usage.
- [ ] Operational documentation distinguishes reclaimed space **inside PostgreSQL** from Windows VHDX compaction, verifies backup availability and database health, and does not recommend pruning production volumes.
- [ ] `PhotoIdentity.Docs validate` and `generate --check` pass, and the maintainer confirms a representative verified-backup run with no accumulating restore database.

## Verification and safety

Use disposable PostgreSQL test databases and non-personal fixtures for automated destructive-path tests. On a maintained system, confirm active production database from the application/connection, maintain a verified logical backup, record the intended generated restore name, and require explicit maintainer authorization before retiring previously retained copies. Collect only aggregate storage/count/status evidence in the repository; never commit database dumps, identities, photos, connection strings, private local paths, or full table contents.

References: [PostgreSQL operations](../../operations/postgresql-operations.md), [WI-0208 Windows acceptance commands](../../operations/postgres-wi0208-verification.md), [WI-0106](WI-0106-postgresql-operations-and-archive-catchup.md), and `verify-postgres-backup-restore.ps1`.

## Separation from backup retention

This workitem only disposes of isolated PostgreSQL **restore-verification databases**. Even after a successful restore check, the input backup and its SHA-256/report remain untouched. [WI-0209](WI-0209-simple-postgres-backup-retention.md) addresses the maintainer's distinct local pre-change and off-disk recovery-backup needs without blocking WI-0208.

## Current status

**In progress, implementation pending CI and maintained-host verification (2026-10-31).** The verifier now creates an ownership-marked isolated database, preserves failures and read-only resumes, commits a passing comparison report before default cleanup, records the exact cleanup outcome, and leaves a copy only with explicit retention. A separate read-only inventory and exact-name, marker-validated disposal tool refuses unmarked legacy copies, live sessions, arbitrary names and production source matches. Schema rehearsal runbooks explicitly request retention. A PowerShell source-free regression suite is integrated into Windows CI; an opt-in isolated real-Podman/real-PostgreSQL fixture is available but has not been run; maintained Windows verification remains outstanding.

**Remaining acceptance evidence:** run CI parser/synthetic tests and docs checks, disposable real PostgreSQL restore success/failed-restore/resume/retention/cleanup refusal paths, then the maintainer's actual backup verification with before/after SQL database/volume sizes. No destructive maintenance on old unmarked databases or Windows VHDX is authorized by this implementation. Do not check off work-item acceptance until these results are observed.
