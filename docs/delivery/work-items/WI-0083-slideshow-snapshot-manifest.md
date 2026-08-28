---
id: WI-0083
title: Add stable Smart Collection slideshow snapshots
milestone: M22
status_source: ../status/work-items.yaml
depends_on: [WI-0050]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite]
---

# WI-0083: Add stable Smart Collection slideshow snapshots

## Objective

Give slideshow playback one complete, immutable ordered view of a saved Smart Collection instead of reusing the Smart Collections workspace's paged live results.

The current workspace page size is not a slideshow contract, and repeated offset queries can shift when collection membership changes. M22 requires stable playback from the collection state that existed when Start slideshow was pressed.

## Contract

- V1 creates slideshow snapshots from saved Smart Collections only.
- Snapshot creation evaluates the saved collection against the catalogue once and materializes the complete ordered immutable revision-ID manifest in one logical database read/transaction.
- The snapshot is unaffected by subsequent edits to collection filters, people, tags, Places or catalogue membership.
- Order is oldest to newest using photographic capture time when available and catalogue observation time as fallback, followed by a deterministic immutable-revision tie break.
- Snapshot creation is not constrained by the Smart Collections workspace's 40-item page size or normal query `limit <= 200` contract.
- A snapshot may contain zero, one or many revisions.
- Do not load/decode image bytes while materializing the manifest.
- Do not expose source paths or filenames in the slideshow manifest.
- Return enough source context for the UI to return deliberately to the originating saved Smart Collection after slideshow exit.
- Resource metadata/pixel URLs may be resolved lazily from revision IDs; the manifest itself should stay lightweight.

## Proposed API shape

The exact route naming may follow existing endpoint conventions, but the logical operation should be explicit, for example:

```text
POST /api/smart-collections/{collectionId}/slideshow-snapshot
```

A response can contain:

```text
collectionId
collectionName
createdAtUtc
items: [ { revisionId, effectiveTakenAt } ... ]
total
```

`effectiveTakenAt` is optional client information; revision ID order is authoritative for the session.

The implementation does not need durable database persistence for a slideshow session. The returned immutable revision-ID list is the snapshot. Browser reload/restart may require starting a new slideshow unless a later work item deliberately adds resume persistence.

## Ordering semantics

For sorting purposes define an effective slideshow time from:

1. `TakenAtLocal` when present;
2. otherwise `ObservedAtUtc`.

Sort ascending by the effective time, then use stable additional fields/revision identity to make ties deterministic. The repository's existing newest-first Smart Collection display order is not reused as the slideshow default.

## Acceptance criteria

- [ ] Snapshot creation works for a saved Smart Collection with zero results.
- [ ] Snapshot creation works for one result and for result counts larger than both the UI page size and the existing 200-item query limit.
- [ ] Every revision that matched the saved Smart Collection at snapshot creation appears exactly once in the manifest.
- [ ] The manifest is deterministic oldest-to-newest according to the documented capture/observation fallback semantics.
- [ ] Changing collection filters or matching photo metadata after snapshot creation does not add, remove or reorder items already returned in that snapshot.
- [ ] Snapshot creation does not request/hydrate originals and does not decode image pixels.
- [ ] Source paths and filenames are absent from the response.
- [ ] The snapshot includes the collection identity/name and total count needed by the presentation lifecycle.
- [ ] Focused persistence/API tests cover zero, one, >200 items, missing capture dates, equal timestamps and mutation after snapshot creation.
- [ ] Existing Smart Collection workspace query ordering/pagination behavior is unchanged unless separately documented.

## Non-goals

- Unsaved/transient preview slideshow entry.
- Shuffle/random order.
- Persisting slideshow sessions across application restarts.
- Loading original/proxy pixels.
- Changing the default ordering of the normal Smart Collections workspace.
