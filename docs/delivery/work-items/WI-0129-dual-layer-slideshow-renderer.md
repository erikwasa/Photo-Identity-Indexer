---
id: WI-0129
title: Add a predecoded dual-layer slideshow transition renderer
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0084, WI-0108]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Web.Tests, docs]
---

# WI-0129: Add a predecoded dual-layer slideshow transition renderer

## Objective

Replace abrupt single-image DOM replacement with a bounded two-layer presentation compositor that stages and decodes the incoming image before performing a smooth transition.

## Why

The current slideshow already prefetches adjacent image resources, but playback still presents one keyed `<img>` at a time. Successful navigation therefore feels like a sequence of hard cuts even when network delivery is fast. A dual-layer renderer is the foundation for visual polish without adding user-facing controls.

## In scope

- Keep one current presentation layer and one incoming/staging layer, with bounded lifecycle and cleanup.
- Reuse the existing prefetch pipeline and explicitly await browser decode readiness where practical before starting the transition.
- Crossfade between layers using a conservative fixed default rather than adding a transition-duration preference.
- Start/restart the image display timer only after the destination image is presentation-ready; loading/decoding/transition setup must not consume the configured viewing duration.
- Preserve manual next/previous, autoplay, looping, one-photo behavior, protected controls, fullscreen recovery and prepared-original playback.
- Define behavior for rapid manual navigation while a transition is in progress: coalesce or serialize requests without exposing intermediate broken frames.
- Preserve a no-animation/reduced-motion path that still swaps only ready images.
- Extend browser diagnostics/tests so transition readiness and visible-image timing can be distinguished from resource-fetch timing.
- Keep decoded/staged image count bounded.

## Out of scope

- User-selectable transition styles.
- Ken Burns/motion treatment.
- Moment-aware transition semantics.
- Changing collection membership or slideshow ordering.

## Acceptance criteria

- [ ] Normal successful advancement does not produce a black flash between two ready images.
- [ ] The incoming image has completed load/decode readiness before it becomes the visible destination where the browser supports explicit decode.
- [ ] Exactly bounded presentation layers are retained; long slideshows do not accumulate decoded DOM/image objects.
- [ ] The configured per-image viewing duration begins after the destination presentation is ready.
- [ ] Rapid taps/swipes do not leave both layers visible incorrectly, skip into an invalid state or permanently stall autoplay.
- [ ] Looping from last to first uses the same transition lifecycle without a special visible reload flash.
- [ ] One-photo looping does not crossfade the image with itself.
- [ ] `prefers-reduced-motion` disables nonessential transition animation while retaining ready-before-show behavior.
- [ ] Existing protected/fullscreen/original-preparation behavior remains functional.
- [ ] Focused automated/browser tests cover autoplay transition, manual navigation, rapid navigation, loop boundary, one-photo and failed-image fallback.

## Verification requirements

Run the existing slideshow unit/browser tests plus a representative phone slideshow with mixed portrait/landscape images. Capture browser diagnostics before/after to ensure the smoother renderer does not reintroduce fetch/decode stalls or unbounded memory behavior.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
