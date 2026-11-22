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

Issue #484 adds a related consumer-UX correction discovered during maintained use: preparation-receipt revalidation must not surface an implementation-level `Retry verification` error. An unverified receipt should simply withhold stale Prepared state and remain available for later revalidation.

## Scope

- Present Smart, manual and Creative slideshows in one continuous consumer-facing library/grid.
- Introduce or extend a presentation-level slideshow-library item model so all backing collection types can share one card surface while preserving type-specific behavior internally.
- Give the slideshow-library experience coherent ownership of Smart, manual and Creative catalogue loading/caching/retry state instead of rendering Creative slideshows as a visibly separate library.
- Remove creation-type section headings and grouping from the consumer page.
- Remove `Manual` and `Creative` as primary consumer-facing card classifications. Keep only metadata that helps choose or understand playback, such as slideshow name, truthful quantity wording, Prepared state and meaningful source context where appropriate.
- Preserve existing cover behavior, launch routing, preparation semantics, session caching, retry behavior, accessibility and phone/PWA responsiveness.
- Preserve deterministic ordering suitable for the later filtering work item.
- Do not require a consumer to manually retry prepared-original receipt verification.

## Consumer UX contract

A slideshow is presented as a slideshow. The consumer does not need to know whether it was authored as a Smart Collection, manual photo list or Creative Collection in order to find and play it.

Creation kind may remain in the internal presentation model when required for launch/preparation routing, but it must not define the primary visual grouping of the library.

Prepared state is shown only after the stored preparation receipt is successfully revalidated. Temporary inability to perform that check is not a consumer error: preserve the receipt, suppress stale Prepared state and try again on a later normal refresh/page entry rather than displaying a dedicated retry prompt.

## Implementation

`SlideshowLibraryCatalogue` now normalizes Smart, manual and Creative records into `SlideshowLibraryItem` values with one deterministic name/ID ordering. The internal kind keeps launch and preparation routing correct, while cover identity, exact manual counts, Creative target counts and optional Creative source context remain explicit fields instead of visual group boundaries.

`UnifiedSlideshowLibrary` renders the entire consumer card grid. Smart/manual catalogue state continues to come from the parent page/session cache; the unified component owns Creative refresh state and combines all cached/fresh sources into one presentation collection. A source refresh failure can preserve and display the entries that are already available rather than creating separate type-specific sections.

Creative cards use the anchor Smart Collection for their cover but launch the Creative Collection identity. They retain truthful `Up to N photos` wording and optional `From <anchor>` context, without a `Creative` classification label. Manual cards retain exact counts without a `Manual` label. Prepared state remains available only for the existing Smart/manual preparation workflow.

The former `CreativeSlideshowLibrary` component and its duplicated card styles are removed. `/slideshows` now hosts one unified library component, while parent-only preparation controls remain separate from the viewing grid.

The prepared-original `VerificationError` notice and `Retry verification` action are no longer rendered. Existing receipt logic still removes stale ready presentation while retaining an unverifiable receipt for a later normal revalidation.

No persistence/database redesign is introduced.

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
- A temporarily unverifiable preparation receipt does not display a stale Prepared badge or a dedicated retry-verification error/prompt.
- The combined layout remains usable on desktop, phone browser and installed PWA without horizontal overflow.
- Keyboard/screen-reader semantics remain correct for the single library.
- Focused component/unit tests cover normalization, deterministic ordering, rendering and type-specific launch behavior at the lowest practical test layer.

## Maintainer verification

After implementation, populate the maintained catalogue with a mix of Smart, manual and Creative slideshows. Verify on desktop and phone/PWA that they appear together as one library, all launch correctly, no type-specific grouping is visible, cached navigation still feels immediate, and retry/error states do not split the library back into separate conceptual sections.

Also reproduce the earlier prepared-original verification-unavailable condition if practical. The slideshow page must not show `Prepared originals could not be verified...` or require `Retry verification`; any unverified Prepared badge should simply be absent until normal revalidation succeeds.

Implementation is submitted in PR #486.

## Maintainer acceptance — 2026-10-04 (Europe/Stockholm)

The maintainer completed the WI-0177 verification after PR #486 was merged and reports that the unified slideshow library works as expected. Smart, manual and Creative slideshows can be consumed through the unified presentation without the previous type-based library split. WI-0177 is accepted and complete, and its dependency on WI-0178 is therefore released.
