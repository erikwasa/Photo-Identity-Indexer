---
id: WI-0145
title: Add explicit photo-list collections for manually assembled slideshows
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0083, WI-0094]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Tests, docs]
---

# WI-0145: Add explicit photo-list collections for manually assembled slideshows

## Objective

Introduce a collection type that stores an explicit ordered set of immutable revision IDs without weakening Smart Collection query semantics.

## Why

A manually assembled slideshow is different from a Smart Collection, which intentionally stores a reusable filter rather than copied revision IDs.

## In scope

- Define a separate explicit/manual collection model with PostgreSQL persistence.
- Store stable revision IDs and deterministic order.
- Expose a snapshot compatible with existing playback.
- Handle missing/excluded revisions safely.
- Keep provenance distinct from query results.

## Out of scope

- Turning Smart Collections into mutable ID lists.
- Collaborative editing.
- Copying photo files into album folders.

## Acceptance criteria

- [x] An explicit collection persists a named ordered revision list.
- [x] Its snapshot uses the existing playback resource boundary.
- [x] Smart Collections remain unchanged.
- [x] Missing/excluded items follow documented safety rules.
- [x] Persistence/API tests cover lifecycle and membership.

## Verification requirements

Core/PostgreSQL/API tests before WI-0146 adds UI.

## Completion notes

- Files changed: Core photo-list collection model/repository contract, PostgreSQL schema v31 and repository, PostgreSQL runtime composition, dedicated API endpoints, focused HTTP contract and live PostgreSQL persistence tests, and delivery tracking.
- Trade-offs: photo-list collections are a separate aggregate and table family from Smart Collections. Membership is a unique ordered list of at most 5,000 immutable revision IDs and updates replace that list atomically. Create/update rejects missing or already source-removed revisions. Stored membership may retain a revision whose asset is later marked removed, but slideshow snapshot creation omits it; hard revision deletion/purge cascades membership removal so privacy deletion wins over album retention. Snapshot order is the stored manual order, not capture-time order.
- Playback boundary: `POST /api/photo-list-collections/{id}/slideshow-snapshot` returns the existing lightweight `SmartCollectionSlideshowSnapshotResponse` shape, so playback continues to resolve pixels/resources lazily from revision IDs without source paths or filenames in the manifest.
- Deferred work: the API is mapped only for the PostgreSQL catalogue; SQLite receives no new compatibility implementation because M29 is already scheduled to remove SQLite runtime support.
- Verification evidence: PR #407 merged on 2026-09-22 and CI run #2095 completed successfully across build/fast tests, both integration shards, living/generated documentation checks, launcher verification and Windows package verification. On 2026-09-23 the maintainer also accepted the combined WI-0145/WI-0146 desktop and phone flow, confirming persisted explicit membership and ordering through the UI.
- Lifecycle: WI-0145 is complete and maintainer-verified.
