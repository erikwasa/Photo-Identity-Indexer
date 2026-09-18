---
id: WI-0152
title: Add safe browser video serving and viewer playback
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0152: Add safe browser video serving and viewer playback

## Objective

Open supported videos in the local viewer with bounded browser-compatible playback.

## Why

Poster-only support is incomplete unless the user can intentionally play media while retaining privacy/hydration boundaries.

## In scope

- Support browser range requests where required.
- Use original playback when compatible/available and define derivative fallback where necessary.
- Add play/pause/seek/duration without regressing images.
- Preserve exclusion, availability, verification and hydration gates.
- Avoid unexpected automatic hydration of large online-only videos.

## Out of scope

- Slideshow integration.
- Background face analysis.
- Public streaming.

## Acceptance criteria

- [ ] Supported local video plays/seeks on phone and desktop.
- [ ] Online-only behavior is explicit and bounded.
- [ ] Excluded/unavailable media cannot bypass access controls.
- [ ] Images keep their existing optimized path.

## Verification requirements

HTTP range/access tests plus real desktop/phone playback verification.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
