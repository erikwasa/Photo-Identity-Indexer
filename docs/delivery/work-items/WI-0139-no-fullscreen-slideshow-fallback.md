---
id: WI-0139
title: Allow slideshow playback when browser fullscreen is unavailable
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0084, WI-0136]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0139: Allow slideshow playback when browser fullscreen is unavailable

## Objective

Make slideshow startup usable on browsers such as iPhone Safari that do not expose the standard element Fullscreen API.

## Why

The current launch path can navigate successfully but the slideshow then pauses whenever fullscreen is inactive. On a browser where `requestFullscreen` is fundamentally unsupported, retrying fullscreen cannot recover, leaving the viewer unable to start normal playback.

## In scope

- Distinguish unsupported fullscreen capability from a supported fullscreen request that was rejected or later lost.
- Allow an immersive full-viewport slideshow fallback when true browser fullscreen is unavailable.
- Keep autoplay/manual navigation usable in fallback mode.
- Preserve stricter recovery when fullscreen is supported but unexpectedly lost.
- Explain reduced protection clearly for protected/toddler mode.
- Cover slideshow-library and Smart/Creative Collection launch paths.

## Out of scope

- Emulating browser chrome removal the platform does not support.
- Weakening fullscreen recovery where the Fullscreen API is available.

## Acceptance criteria

- [ ] A browser with no standard fullscreen capability can start and play instead of remaining unrecoverably paused.
- [ ] Supported browsers still request true fullscreen from the activating gesture.
- [ ] Protected mode reports reduced browser-level protection while retaining application-level parent controls.
- [ ] Tests distinguish unsupported, rejected and lost-fullscreen states.
- [ ] Maintainer verification covers the reported iPhone path and a desktop browser.

## Verification requirements

Automated browser/interop state tests plus maintainer verification on the affected iPhone/browser path.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
