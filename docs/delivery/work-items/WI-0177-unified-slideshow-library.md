---
id: WI-0177
title: Unify slideshow library into one consumer-facing grid
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: [WI-0108, WI-0158, WI-0172, WI-0173]
related_adrs: []
affected_modules: [PhotoIdentity.Web, docs]
---

# WI-0177: Unify slideshow library into one consumer-facing grid

## Context

Issue #480 records a consumer-navigation problem on `/slideshows`: Smart/manual slideshows are currently presented separately from Creative slideshows even though the person choosing something to watch experiences all of them simply as slideshows. As the library grows, creation-mechanism grouping adds cognitive overhead without helping playback.

The slideshow surface is already intentionally viewing-only; editing and deletion belong in Full app -> Manage collections. Preserve that boundary.

## Scope

- Present Smart, manual and Creative slideshows in one continuous consumer-facing library/grid.
- Introduce or extend a presentation-level slideshow-library item model so all backing collection types can share one card surface while preserving type-specific behavior internally.
- Give the parent slideshow-library experience coherent ownership of Smart, manual and Creative catalogue loading/caching/retry state instead of rendering Creative slideshows as a visibly separate library.
- Remove creation-type section headings and grouping from the consumer page.
- Remove `Manual` and `Creative` as primary consumer-facing card classifications. Keep only metadata that helps choose or understand playback, such as slideshow name, truthful quantity wording, Prepared state and meaningful source context where appropriate.
- Preserve existing cover behavior, launch routing, preparation semantics, session caching, retry behavior, accessibility and phone/PWA responsiveness.
- Preserve deterministic ordering suitable for the later filtering work item.

## Consumer UX contract

A slideshow is presented as a slideshow. The consumer does not need to know whether it was authored as a Smart Collection, manual photo list or Creative Collection in order to find and play it.

Creation kind may remain in the internal presentation model when required for launch/preparation routing, but it must not define the primary visual grouping of the library.

## Implementation direction

The existing Smart/manual library already normalizes two persistence shapes into one page-level entry model. Extend that pattern so Creative entries participate in the same presentation collection. Prefer one parent-owned loading state and one rendered grid while retaining type-specific launch information behind the normalized model.

Do not introduce a persistence/database redesign solely to unify presentation.

## Out of scope

- Search/filter/sort controls; tracked by WI-0178.
- Changes to Smart, manual or Creative authoring workflows.
- Moving edit/delete actions back into the slideshow consumer surface.
- Semantic/photo-content search.

## Acceptance criteria

- `/slideshows` renders Smart, manual and Creative slideshows in one continuous library with no creation-type sections.
- A consumer can choose and play every existing slideshow type without understanding its creation mechanism.
- Smart/manual/Creative type remains only where internally necessary for correct playback/preparation routing.
- Consumer card metadata remains truthful and useful; creation-type labels are not the primary explanatory text.
- Loading, cached display, refresh and retry behavior remain coherent when one catalogue request is temporarily unavailable.
- Existing Prepared/count semantics and cover reuse are not regressed.
- The combined layout remains usable on desktop, phone browser and installed PWA without horizontal overflow.
- Keyboard/screen-reader semantics remain correct for the single library.
- Focused component/unit tests cover normalization, deterministic ordering, rendering and type-specific launch behavior at the lowest practical test layer.

## Maintainer verification

After implementation, populate the maintained catalogue with a mix of Smart, manual and Creative slideshows. Verify on desktop and phone/PWA that they appear together as one library, all launch correctly, no type-specific grouping is visible, cached navigation still feels immediate, and retry/error states do not split the library back into separate conceptual sections.
