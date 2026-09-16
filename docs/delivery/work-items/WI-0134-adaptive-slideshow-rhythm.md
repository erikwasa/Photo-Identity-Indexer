---
id: WI-0134
title: Add bounded adaptive timing for a less mechanical slideshow rhythm
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0129]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Web, PhotoIdentity.Web.Tests, docs]
---

# WI-0134: Add bounded adaptive timing for a less mechanical slideshow rhythm

## Objective

Treat the persisted image-duration setting as the viewer's general pace while allowing small deterministic presentation-time adjustments that make autoplay feel less metronomic.

## Why

A rigid five-seconds-per-frame cadence can feel mechanical even after cuts become smooth. Small bounded differences can create a more natural rhythm without asking the user to configure per-photo timing.

## In scope

- Define a simple deterministic timing policy around the configured duration.
- Keep variation narrow enough that a 5-second preference still clearly feels like a 5-second slideshow.
- Allow stable signals such as chapter entry, image complexity proxies already available, group-photo/face count, or presentation annotations to justify small adjustments when evidence is reliable.
- Use the configured duration unchanged as fallback.
- Keep manual-navigation timer semantics predictable: a manually selected destination receives a fresh full policy duration only after it is visible.
- Do not introduce random timing that changes run-to-run.
- Do not add normal UI controls for min/max variation.

## Out of scope

- ML aesthetic scoring.
- Audio beat synchronization.
- Changing the persisted duration preference itself.

## Acceptance criteria

- [x] The configured duration remains the dominant pace and bounds all automatic adjustments.
- [x] The same image/context produces the same effective duration under the same policy version.
- [x] Missing/uncertain presentation evidence falls back to the configured duration.
- [x] Manual navigation and pause/resume retain understandable timer behavior.
- [x] Effective timing can be diagnosed/tested without exposing it as normal viewer chrome.
- [x] Automated tests protect bounds, determinism and fallback semantics.

## Verification requirements

Compare long representative runs with fixed timing and adaptive timing; retain only adjustments that reduce mechanical cadence without making viewers wonder when the next image will arrive.

## Completion notes

- Files changed: `SlideshowTimingPolicy.cs`, `SlideshowPlaybackState.cs`, `SlideshowPresentation.razor`, `Slideshow.razor`, timing/playback integration tests, and delivery-status documentation.
- Trade-offs: the first policy deliberately uses only reliable face-count evidence already fetched for presentation. Single-face frames run at 98% of the configured duration, two-face frames at 104%, groups at 108%, and no-face/unavailable evidence at exactly 100%. This keeps the configured preference dominant and avoids a new settings surface or random timing.
- Deferred work: the required representative fixed-versus-adaptive long-run rhythm comparison remains a maintainer/device review item. If the rhythm feels distracting, tune the fixed multipliers rather than adding viewer controls.
- Commands run: the initial PR workflow built successfully and all test/integration shards passed. Its generated-document check failed because `work-items.yaml` and `work-items-index.md` were stale; the PR update regenerates those views while also incorporating current `main`.
