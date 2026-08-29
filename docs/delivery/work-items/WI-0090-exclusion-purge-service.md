---
id: WI-0090
title: Purge excluded photo data and derivatives safely
milestone: M23
status_source: ../status/work-items.yaml
depends_on: [WI-0089]
related_adrs: [ADR-0008]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Worker, PhotoIdentity.Api, documentation]
---

# WI-0090: Purge excluded photo data and derivatives safely

## Objective

Turn source-copy exclusion into a real privacy purge by removing Photo Identity's retained photo-specific files and revision-linked database state through an idempotent, crash-safe workflow.

## Why

SQLite cascades can remove linked records but cannot delete proxy/crop files on disk. Deleting database references before deleting those files could strand sensitive orphaned artifacts. Conversely, a partial filesystem failure must not make the excluded photo accessible again.

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
- [ ] Automated tests exercise filesystem and SQLite cleanup together, not database cascades alone.

## Verification requirements

Focused integration tests must use generated safe test images and temporary derivative roots. Include injected failures before/after filesystem deletion and before/after database cleanup to prove restart safety and idempotency.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
