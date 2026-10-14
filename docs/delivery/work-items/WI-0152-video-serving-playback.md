---
id: WI-0152
title: Add safe browser video serving and viewer playback
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151, WI-0189]
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
- Prefer the already-created, locally retained WI-0189 playable proxy; use original only when explicitly compatible, available and authorized.
- Serve through existing guarded APIs, not an exposed cache directory.
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
- [ ] Playback does not unexpectedly hydrate a full original when a current proxy is present; evicted/stale proxies degrade visibly.

## Verification requirements

HTTP range/access tests plus real desktop/phone playback verification.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
