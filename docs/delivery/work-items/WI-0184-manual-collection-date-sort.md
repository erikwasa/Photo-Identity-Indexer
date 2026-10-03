---
id: WI-0184
title: Add whole-collection chronological sorting for manual collections
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0184: Add whole-collection chronological sorting for manual collections

## Context

Issue #492 requests a fast way to reorder an entire manual slideshow collection, especially oldest-to-newest or newest-to-oldest by capture time. Manual collections already persist an explicit ordered `RevisionIds` sequence, while the current editor only offers one-step up/down moves.

The new actions should remain lightweight and preserve manual curation rather than turning the editor into a complex playlist tool.

## Scope

- Add compact `Oldest first` and `Newest first` whole-collection ordering actions to `/manual-collections/{id}`.
- Resolve capture timestamps for collection members with bounded/set-oriented catalogue work rather than one browser request per photo.
- Reorder the existing explicit revision-ID sequence and persist it through the normal manual-collection contract.
- Sort dated photos by `TakenAtLocal`/accepted capture-time evidence used elsewhere in the catalogue.
- Place photos with no usable capture timestamp after dated photos in both sort directions so undated photos do not unexpectedly dominate either end.
- Use deterministic stable tie-breaking for equal timestamps and undated items.
- Keep existing per-photo up/down controls available after automatic sorting for final manual adjustment.
- Preserve immutable running slideshow snapshots; reordering affects future slideshow sessions only.
- Keep the action usable on phone/PWA and avoid adding a large sorting panel.

## Architecture constraints

Do not infer or persist new capture metadata merely to sort a collection. Use existing catalogue evidence. Do not reorder a running slideshow snapshot in place.

## Out of scope

- Drag-and-drop playlist editing.
- Arbitrary multi-key sort builders.
- Sorting Smart or Creative Collections through the manual collection editor.
- Replacing explicit manual order with a persisted dynamic sort expression.

## Acceptance criteria

- A manual collection can be reordered oldest-first with one action.
- The same collection can be reordered newest-first with one action.
- Capture timestamps are resolved in a set-oriented/bounded way suitable for realistically sized manual collections.
- Undated photos appear after dated photos for either direction and ties are deterministic.
- The resulting order is persisted in the existing explicit `RevisionIds` sequence and survives reload/restart.
- Individual up/down moves continue to work after an automatic sort.
- A slideshow already running from an immutable snapshot is unchanged; a subsequent session uses the newly saved order.
- Focused API/persistence/Web tests plus maintainer desktop and phone/PWA verification cover both sort directions and undated/tied timestamps.

## Implementation notes

Implemented on `agent/WI-0184-manual-collection-date-sort` in PR #497.

- The Web editor sends one `POST /api/photo-list-collections/{id}/sort` request for either `oldest-first` or `newest-first`; it does not issue per-photo metadata requests.
- `PostgresPhotoListCollectionCaptureTimeRepository` resolves the full requested revision batch in one PostgreSQL query. It follows the existing effective capture-date policy: the latest manual `set` action wins, otherwise extracted `photo_capture_metadata.taken_at_local` is used, and missing evidence remains undated.
- For imprecise manual year/month evidence, chronological ordering uses the start of the accepted range (January 1 for year precision, the first day of the month for month precision). Day precision uses that day; extracted timestamps retain their full wall-clock time.
- `PhotoListCollectionChronologicalOrdering` keeps undated photos after all dated photos in either direction and preserves the collection's prior relative order for equal timestamps and for undated photos.
- The sorted sequence is persisted through the existing `IPhotoListCollectionRepository.UpdateAsync` path. No schema migration or persisted dynamic sort mode was added.
- Existing per-photo up/down controls remain available after sorting.
- Snapshot tests capture a manual slideshow snapshot before reordering, then verify it remains unchanged while a later snapshot observes the newly persisted order.

## Automated verification

Focused coverage added in:

- `PhotoListCollectionChronologicalOrderingTests` for oldest/newest direction, stable timestamp ties and undated-last behavior.
- `PostgresPhotoListCollectionCaptureTimeRepositoryTests` for one ordered batch containing extracted metadata, a manual-date override and an undated revision.
- `PhotoListCollectionSortEndpointTests` for the one-request API flow, persisted order, stable ties, undated-last behavior, invalid directions and immutable prior snapshots.
- `ManualCollectionChronologicalSortUiTests` for the two compact Web actions, server-side sort request and continued availability of manual up/down moves.

CI and maintainer verification remain required before completion.

## Maintainer verification

After PR #497 CI passes, verify `/manual-collections/{id}` on desktop and phone/PWA with a representative collection containing dated, undated and at least two equal-date photos:

1. Run `Oldest first`; dated photos should be ascending and undated photos should remain last.
2. Run `Newest first`; dated photos should be descending and undated photos should still remain last.
3. Confirm equal-date photos retain their previous relative order.
4. Reload the page and confirm the sorted explicit order persisted.
5. Use ↑/↓ after sorting and confirm manual fine-tuning still persists.
6. If practical, start a slideshow, reorder the source collection separately, and confirm that already-running snapshot does not change while a newly started session uses the new saved order.
7. Confirm the two sort buttons remain compact and usable without horizontal overflow on phone/PWA.
