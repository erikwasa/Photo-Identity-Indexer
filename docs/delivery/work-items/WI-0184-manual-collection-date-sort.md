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
