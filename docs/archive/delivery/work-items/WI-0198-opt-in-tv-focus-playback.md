---
id: WI-0198
title: Integrate opt-in TV-focus slideshow mode with quality and performance safeguards
milestone: M38
status_source: PhotoIdentity.Docs
depends_on: [WI-0197]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0198: Integrate opt-in TV-focus slideshow mode with quality and performance safeguards

## Objective

Expose a reversible, browser/device-local Standard / TV Focus presentation setting and integrate the WI-0197 framing policy into existing slideshow playback without regressing accepted M27 behaviors.

## In scope

- Persist a simple opt-in image-framing setting; Standard contain/blurred-backdrop stays the default, independent of screen-orientation preference.
- Apply safe subject crop only where planning succeeds. Compose with bounded A/B presentation layers, synchronized background and predecoded crossfades.
- Preserve fullscreen/protected parent mode, captions, slideshow membership, exposure counts, autoplay, manual navigation, adaptive dwell timing, pause/resume, looping, failed-image fallback and reduced-motion path.
- Reuse the same served image URL and avoid implicit original hydration or unbounded extra network/image decoding.
- Bound zoom by proxy/original source resolution and do not reintroduce WI-0171 prepared-original coarse-pointer phone raster/grid seams. Prefer static/contain mobile fallback where unsafe.
- Compare memory, browser timing, network requests and fallback behavior.

## Out of scope

Default-on framing, per-collection/per-photo crop authoring, context-to-detail choreography, extra ML models and image rewrites.

## Acceptance criteria

- [ ] Standard behavior and stored preference compatibility are unchanged unless a user opts into TV Focus.
- [ ] TV Focus applies safe plans, visibly enlarges suitable subjects, and contains unsuitable images.
- [ ] Crossfade, readiness, timer, navigation, pause, looping and parent/recovery tests pass.
- [ ] Reduced motion and WI-0171 affected mobile-original conditions remain safe.
- [ ] Bounded resource metrics show no progressive slowdown, seam artifacts or additional unbounded downloads.
- [ ] Gate A: maintainer judges static enlargement from the real TV before optional WI-0199/WI-0200 experiments proceed.

## Verification

Existing slideshow integration/JavaScript tests, 16:9 TV and phone-landscape smoke reviews, proxy/original comparisons and browser diagnostics.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
