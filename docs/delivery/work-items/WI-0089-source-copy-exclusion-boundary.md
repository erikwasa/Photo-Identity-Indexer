---
id: WI-0089
title: Add durable source-copy exclusion and access enforcement
milestone: M23
status_source: ../status/work-items.yaml
depends_on: [WI-0041, WI-0042]
related_adrs: [ADR-0008]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Worker, documentation]
---

# WI-0089: Add durable source-copy exclusion and access enforcement

## Objective

Introduce a durable source-copy-specific exclusion state that immediately makes one source locator inaccessible and non-processable through Photo Identity while leaving the OneDrive/source original untouched.

## Why

A private photo can legitimately remain backed up in personal OneDrive while being unsuitable for an application used by other people. A normal visibility filter is insufficient because Photo Identity could still retain/serve originals, proxies, face crops, embeddings or metadata.

## In scope

- Persist exclusion against one source plus normalized source key/path, independently of content hash and duplicate grouping.
- Make exclusion durable before any purge work starts.
- Keep exclusion distinct from automatic source presence/missing state.
- Ensure the same excluded locator stays excluded across restart/rescan and if that locator later reappears.
- Do not propagate exclusion to exact duplicates at other paths.
- Do not carry exclusion across a move/rename; the new path is independently included until explicitly excluded.
- Add a central enforceable exclusion check below UI-only filtering.
- Immediately deny excluded content from analysis/job scheduling, detector/embedding/identity regeneration, metadata/tag/Places enrichment, derivative generation, Smart Collections, slideshow manifests, photo/face review, original/proxy media serving, explicit original viewing and managed hydration.
- Permit only the minimal archive/status operations needed to show the source locator as excluded and manage purge/retry/restore.
- Ensure normal scanning can observe the excluded locator's presence without reopening it for analysis/content extraction.

## Out of scope

- Actual filesystem/database purge; WI-0090 owns deletion.
- Deleting the source original.
- Content-hash denylisting.
- Excluding all exact duplicates in one implicit action.
- UI workflows beyond the minimum enforcement/status plumbing; WI-0091 owns operator UX.

## Acceptance criteria

- [x] Exclusion is keyed to one source copy/locator and is durable across restart.
- [x] Excluding one of two exact duplicate paths does not exclude the other.
- [x] A moved/renamed excluded file is not automatically excluded at its new path.
- [x] A source file at an actively excluded locator cannot be scheduled for new photo, face, metadata, place or identity work.
- [x] Smart Collections, slideshow manifests and normal review/library queries cannot return excluded content.
- [x] Original/proxy/hydration endpoints reject excluded content even if a caller holds a previously valid opaque revision/resource identifier.
- [x] The excluded original is never modified or deleted.
- [x] Exclusion remains effective while purge is pending or failed.
- [x] Repeated scans do not recreate revisions/proxies/analysis for the excluded locator.
- [x] Logging and API errors do not expose private source paths or photo content.

## Verification requirements

Automated cross-layer tests are required for scheduler/query exclusion and direct media-resource denial, including a request made with an identifier captured before exclusion. Add tests for duplicate independence, same-locator persistence and moved-excluded-path behavior.

## Completion notes

- Files changed: added the provider-neutral source-copy exclusion contract and SQLite/PostgreSQL persistence; enforced exclusion in archive scanning/move reconciliation, processing claims, metadata/place/identity paths, review/library/Smart Collection queries, photo-list collections, proxy/original/hydration access and minimum archive exclusion status endpoints; added integration and persistence coverage.
- Trade-offs: the durable tombstone is intentionally keyed only by normalized `(source_id, source_key)` and minimal purge state, not by content hash; restore explicitly removes that locator tombstone. Physical derivative/catalogue deletion is intentionally not part of this boundary.
- Deferred work: WI-0090 owns crash-safe purge/deletion and WI-0091 owns the complete operator exclusion/purge/retry UX.
- Commands run: implementation workflow `36198419182` passed the Release build, fast test assemblies, both required integration shards, documentation validation/generated-file check, published review verification, Windows mixed-media verification, package verification and launcher verification. Final-head workflow `36199153432` passed the same required repository gate after lifecycle closeout.
- Merge: PR #428 merged to `main` as commit `26ee8e0a60f14aad5b9a1d3bb970f878328f7cd3` on 2026-09-25.
