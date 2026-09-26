---
id: WI-0090
title: Purge excluded photo data and derivatives safely
milestone: M23
status_source: ../status/work-items.yaml
depends_on: [WI-0089]
related_adrs: [ADR-0008]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Worker, PhotoIdentity.Api, documentation]
---

# WI-0090: Purge excluded photo data and derivatives safely

## Objective

Turn source-copy exclusion into a real privacy purge by removing Photo Identity's retained photo-specific files and revision-linked database state through an idempotent, crash-safe workflow.

## Why

Database cascades can remove linked catalogue rows but cannot delete proxy/crop files on disk. Deleting database references before deleting those files could strand sensitive orphaned artifacts. Conversely, a partial filesystem failure must not make the excluded photo accessible again. PostgreSQL is the authoritative writable production catalogue; retained SQLite support is compatibility/migration tooling and must not be treated as sufficient production-provider verification.

## In scope

- Add durable purge lifecycle state sufficient to represent pending, attempting, complete and failed/retryable cleanup.
- Build a durable purge manifest or equivalent inventory before discarding the paths needed to locate filesystem artifacts.
- Delete all known files derived from the excluded asset/revisions, including archive review proxies, face-review derivatives, face crops and detector/reconciliation inspection crops or equivalent persisted temporary derivatives.
- Remove revision-linked database state including face occurrences/observations, embeddings, identity suggestions/rankings, photo-linked face/person assignments and review actions, manual photo/person associations, photo tags and Places/location actions, EXIF/capture/photo metadata, analysis/proxy completion records, directly referencing processing/reconciliation state, asset revisions and content hashes.
- Preserve shared Person entities and unrelated history belonging to other photos.
- Finish with only the minimal source-locator exclusion tombstone and purge operational/audit state required by ADR-0008.
- Make retries safe if any file/row was already deleted.
- Keep media/processing access blocked for the entire purge lifecycle.
- Verify cleanup rather than treating a requested file deletion as complete without checking the filesystem result.
- Provide privacy-safe diagnostics/counts without logging source paths, person names, hashes or photo content.

## Out of scope

- Secure filesystem overwrite guarantees beyond normal OS/filesystem deletion.
- Deleting OneDrive/source originals.
- Purging merely because a source is missing.
- Retaining/restoring old identifications after re-inclusion.

## Acceptance criteria

- [ ] Starting purge after exclusion cannot make the source copy accessible again even if the process crashes.
- [ ] Known proxy, face-derivative and crop files are deleted before the durable references needed to find them are discarded.
- [ ] Revision-linked face, embedding, suggestion, assignment, tag, place, metadata and analysis state is removed.
- [ ] Shared Person records and data attached only to other photos remain intact.
- [ ] Final exclusion state retains no photo revision/content hash, dimensions, location, tags, face occurrences, embeddings or identity links.
- [ ] A crash/restart at representative purge checkpoints resumes/retries safely.
- [ ] Repeating purge is idempotent when files or rows have already disappeared.
- [ ] Locked/unavailable derivative files result in visible retryable purge failure/pending state rather than silent success.
- [ ] Purge completion verifies that no known local derivative files for the excluded source copy remain.
- [ ] Automated tests exercise filesystem and PostgreSQL catalogue cleanup together; retained SQLite compatibility coverage does not substitute for production-provider coverage.

## Verification requirements

Focused integration tests must use generated safe test images and temporary derivative roots. Include injected failures before/after filesystem deletion and before/after database cleanup to prove restart safety and idempotency. Production-provider verification must cover PostgreSQL cleanup; equivalent SQLite coverage should be retained only where that compatibility path remains supported.

## Completion notes

- Files changed:
  - `src/PhotoIdentity.Core/Sources/ISourceCopyExclusionRepository.cs` and `ISourceCopyPurgeRepository.cs` define the durable purge lifecycle, manifest and provider-neutral purge boundary.
  - `src/PhotoIdentity.Persistence.Postgres/PostgresSourceCopyExclusionRepository.cs` and `PostgresSourceCopyPurgeRepository.cs` persist purge state/manifest, inventory completed and partial analysis output plus detector output, and remove restrictive reconciliation/review-history links before deleting the locator-owned asset graph.
  - `src/PhotoIdentity.Persistence.Sqlite/SqliteSourceCopyExclusionRepository.cs` and `SqliteSourceCopyPurgeRepository.cs` retain equivalent compatibility behavior, including dependency-safe review-action cleanup and partial-analysis output discovery.
  - `src/PhotoIdentity.Api/SourceCopyPurgeService.cs` provides retryable manifest-first filesystem deletion, post-delete verification, catalogue cleanup, idempotency and privacy-safe failure codes; `ArchiveAdvancementHostedService.cs` drains purge work even when ordinary archive advancement is idle.
  - Integration coverage exercises real temporary files, crash/retry behavior, locked-file failure, restrictive review history, partial archive-analysis output under a configured non-default root, and a conditional live-PostgreSQL end-to-end purge preserving a shared Person and an unrelated photo.
- Trade-offs:
  - The durable source-copy exclusion tombstone remains after successful purge and intentionally retains only the source locator plus purge operational state; restore is blocked until purge reaches `completed`.
  - Provider cleanup explicitly retires detector reconciliation plans and review-action dependency chains before asset deletion because those schemas contain restrictive foreign keys that cannot rely on the ordinary revision cascade.
  - Analysis manifests include output directories from both completed `asset_revision_analysis` rows and archive-analysis `processing_jobs`, so interrupted/failed runs do not strand partial files; crop paths also resolve their processing-run `outputRoot` when no completion row exists.
  - Ordinary archive analysis is synchronously advanced behind the archive advancement gate, while exclusion-aware processing claims/original access prevent new work for an excluded revision. This keeps the manifest/deletion sequence aligned with the active in-process archive workflow.
- Deferred work:
  - WI-0091 owns the operator-facing Excluded / Purge pending / Purge failed review workflow.
  - Final WI-0090 completion remains gated on executing the live PostgreSQL verification entry point against a real test PostgreSQL instance; standard GitHub CI does not provide that external database.
- Commands run:
  - GitHub Actions CI is used for compilation, normal integration coverage and documentation validation on PR #431.
  - Live PostgreSQL verification is wired into the existing `verify-postgres.ps1` integration filter through `PostgresRuntimeApplicationTests_SourceCopyPurge`; the maintainer command is `./verify-postgres.ps1 -SkipContainerStart` when the test PostgreSQL instance is already running (or `./verify-postgres.ps1` when the script should start it).
