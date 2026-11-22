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

On 2026-10-04 the maintainer accepted WI-0177 on the maintained slideshow surface. The dependency is satisfied and WI-0178 is ready for implementation.

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

## Implementation — 2026-10-04

`UnifiedSlideshowLibrary` now exposes a compact magnifier button above the unified grid. Activating it reveals one inline search field and moves keyboard focus into that field after render. The input filters immediately on `oninput`; Escape or the clear action removes the query, collapses the field and restores the already-loaded library without making another catalogue request.

`SlideshowLibraryFilter` owns the presentation-only matching rule. It trims surrounding whitespace, matches slideshow names with `OrdinalIgnoreCase`, and preserves the input catalogue order. It deliberately does not search creation kind or Creative anchor/source metadata, so consumers are never asked to navigate by Smart/manual/Creative implementation categories.

No new sort control or persisted sort metadata is introduced. `SlideshowLibraryCatalogue.Combine` already supplies deterministic case-insensitive alphabetical name ordering with stable ID/kind tie-breaks, which satisfies the required consumer-friendly alphabetical ordering without inventing unreliable recent-use/creation semantics.

The search surface uses an accessible search landmark and labels, a labelled clear action, visible focus treatment and minimum 44-pixel-equivalent controls. Its width is bounded by the available container so phone/PWA layouts cannot grow horizontally. A zero-match search replaces the grid with a clear no-results message and recovery action while leaving the underlying catalogue untouched.

Focused `SlideshowLibraryFilterTests` cover whitespace normalization, case-insensitive partial-name matching, mixed Smart/manual/Creative results, preservation of catalogue ordering, restoration for an empty query, and the rule that source/type metadata is not searched. Existing catalogue tests continue to own deterministic combined ordering.

## Maintainer verification

Verify with a sufficiently populated mixed slideshow library on desktop and phone/PWA:

1. Confirm the library initially shows only the compact search icon and remains alphabetically ordered.
2. Open search and confirm focus moves into the input.
3. Search by full and partial slideshow names, mixed casing, and names entered with leading/trailing spaces; results should update immediately across Smart, manual and Creative slideshows.
4. Confirm names are the only search surface: creation type and Creative source context should not unexpectedly match.
5. Clear the query and confirm the full already-loaded library returns immediately without a visible catalogue reload.
6. Enter a query with no matches and confirm the no-results state is understandable and its clear action restores the grid.
7. Press Escape while the search field is active and confirm the query clears and the compact control returns.
8. Repeat on phone/PWA and desktop, checking touch targets, keyboard focus, screen-reader labels and absence of horizontal overflow.

## Maintainer acceptance — 2026-10-04 (Europe/Stockholm)

The maintainer completed the maintained desktop and phone/PWA verification and reports WI-0178 works as expected. The compact search interaction, focus behavior, full/partial/case/whitespace matching, clear/Escape recovery, no-results state, alphabetical ordering and responsive layout are accepted. WI-0178 is complete.
