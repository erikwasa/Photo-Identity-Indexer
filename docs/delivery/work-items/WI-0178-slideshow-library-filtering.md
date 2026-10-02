---
id: WI-0178
title: Add minimal slideshow library filtering and sorting
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: [WI-0177]
related_adrs: []
affected_modules: [PhotoIdentity.Web, docs]
---

# WI-0178: Add minimal slideshow library filtering and sorting

## Context

Issue #481 tracks navigation of a slideshow library that may contain many saved entries. A plain grid becomes slower to scan as the library grows, but the consumer surface should remain visually quiet and should not turn into a collection-management interface.

WI-0177 first unifies Smart, manual and Creative slideshows into one consumer-facing catalogue. This item adds lightweight navigation over that unified catalogue.

## Scope

- Add a compact search/filter affordance to `/slideshows` that is visually minimal when inactive.
- Filter the unified slideshow catalogue immediately as the consumer types, using slideshow name as the initial searchable field.
- Keep filtering client-side for the expected maintained library scale; do not add a database/API search path without measured evidence that the in-memory approach is insufficient.
- Make filtering case-insensitive and normalize leading/trailing whitespace predictably.
- Provide an obvious way to clear the active query and restore the full library without reloading catalogue data.
- Preserve one continuous result grid while filtering.
- Keep deterministic alphabetical name ordering available. A compact sort affordance may expose additional ordering only when reliable metadata already exists and can be used without broadening persistence scope.
- Provide an understandable no-results state.
- Preserve accessible labels, keyboard interaction, focus behavior and phone/PWA touch usability.

## Product constraint

Do not make Smart, manual or Creative creation kind the primary filter/navigation model. The consumer should find a slideshow using consumer-facing information such as its name rather than implementation categories.

A collapsed/revealable search control is preferred over a permanently large filter panel, modal, drawer or dedicated filtering page.

## Out of scope

- Collection editing/deletion/management filters.
- Full-text search across photo captions, people, tags, dates or semantic visual content.
- Server-side search, pagination or virtualization unless profiling of the maintained library demonstrates a need.
- New persisted recent-use or creation timestamps solely to support sorting.

## Acceptance criteria

- A consumer can reveal the search control, type part of a slideshow name and immediately filter the complete unified library.
- Clearing the query restores all loaded slideshows without a catalogue reload.
- Matching is case-insensitive and surrounding whitespace does not produce surprising results.
- Filtering never requires or foregrounds Smart/manual/Creative creation type.
- Default ordering is deterministic and consumer-friendly; alphabetical name ordering is supported.
- No-results state clearly indicates that the current search has no matches and offers a simple recovery path.
- Search/filter controls remain compact on desktop and phone/PWA and do not introduce horizontal overflow.
- Keyboard, focus and screen-reader behavior are covered by focused tests.
- Filtering/sorting is implemented at the presentation layer and does not require persistence redesign for the expected catalogue size.

## Maintainer verification

After WI-0177 is implemented, verify with a sufficiently populated mixed slideshow library on desktop and phone/PWA. Search by full and partial names, mixed casing and surrounding spaces; clear the query; confirm all slideshow types participate in the same results; and verify the controls remain visually secondary to the slideshow cards.
