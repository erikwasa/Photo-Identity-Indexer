---
id: WI-0135
title: Redesign the slideshow library as a visual tap-to-play gallery
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0094]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Web.Tests, docs]
---

# WI-0135: Redesign the slideshow library as a visual tap-to-play gallery

## Objective

Make `/slideshows` feel like a collection of memories rather than an operator screen by giving each slideshow a representative cover and making the collection card itself the primary start action.

## Why

The current read-only library is intentionally safe but still exposes textual cards, explicit Start buttons, preparation actions and a settings disclosure. The desired consumption flow is simpler: open the library, choose something visually and start watching.

## In scope

- Add an automatic representative cover image for each saved slideshow/Smart Collection with deterministic fallback.
- Redesign cards around cover image + collection title, with the whole accessible card acting as the primary Start slideshow action.
- Retain keyboard/screen-reader accessibility and clear focus states.
- Remove redundant primary-action chrome where the card itself communicates play.
- Keep global settings available to the parent/operator but visually secondary to browsing; do not add new playback-style controls.
- Avoid requiring a manually selected cover image.
- Define graceful empty/loading/error states that remain consumer-oriented.
- Allow later M26 selection/anchor logic to improve cover choice without making M26 a dependency.

## Out of scope

- Editing Smart Collection definitions from the read-only library.
- Per-collection slideshow settings.
- Manual cover-photo management.
- Authentication/authorization changes.

## Acceptance criteria

- [x] Every collection card has a deterministic visual cover or neutral fallback.
- [x] Activating the card starts the slideshow without requiring a separate visible Start button.
- [x] Settings/recovery remain available but do not dominate the normal browsing surface.
- [x] Cards remain accessible by keyboard and assistive technology.
- [x] Loading/preparation state does not cause the card layout to jump excessively.
- [x] The page remains read-only with respect to collection/photo metadata.
- [x] Focused UI tests cover card activation, fallback cover, accessibility semantics and start failure recovery.

## Verification requirements

Review on desktop and iPhone-sized viewport. The happy path should read visually as “choose a slideshow” rather than “operate slideshow controls.”

## Completion notes

- Files changed: `Slideshows.razor`, isolated gallery/cover CSS, `SlideshowLibraryCover`, `SlideshowLibraryPresentation`, focused integration tests and delivery handoff/status views.
- Trade-offs: cover discovery reuses the existing saved-collection query contract and leaves the fast slideshow-definition list unchanged. The first deterministic query result (currently the newest matching photo) is used until M26 can provide stronger anchor selection. Cover failure is decorative-only and falls back without blocking playback.
- Deferred work: WI-0136 will further remove routine original-preparation operations from the happy path; M26 may later improve cover choice.
- Commands run: repository CI for PR #347 plus maintainer manual review.
- Verification: on 2026-09-17 the maintainer completed the combined desktop/phone slideshow review and reported WI-0135 works as expected.
