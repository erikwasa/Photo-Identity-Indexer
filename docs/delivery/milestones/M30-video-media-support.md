---
id: M30
title: Video media support
status_source: ../status/milestones.yaml
depends_on: [M19, M22, M24, M27]
---

# M30: Video media support

## Scheduling status

**Intentionally held for later.** Video is a planned direction, but implementation must not begin until the maintainer explicitly reactivates WI-0150/M30. Current work should take the direction into account when changing core asset/media contracts without introducing video dependencies or speculative complexity now.

## Outcome

Photo Identity can eventually catalogue, browse, search and present supported family videos alongside photos while preserving local-first, read-only-original, bounded-hydration, privacy and provenance guarantees.

## Design principles to preserve now

- Keep source/asset/revision identity media-neutral where inexpensive and semantically correct.
- Keep image-specific decoding, face observations, crops and proxies explicit until evidence justifies generalization.
- Do not add FFmpeg or video runtime dependencies before M30 activation.
- Do not make metadata/collection concepts unnecessarily photo-only when they naturally apply to any media revision.
- Treat range serving, codec compatibility, large-file hydration and transcoding as first-class concerns.
- Preserve originals unchanged.

## Work items

- [WI-0150](../work-items/WI-0150-video-architecture-activation-gate.md) - explicit activation/design gate; intentionally blocked.
- [WI-0151](../work-items/WI-0151-video-ingestion-metadata-posters.md) - ingest supported videos and derive metadata/posters.
- [WI-0152](../work-items/WI-0152-video-serving-playback.md) - bounded browser serving/viewer playback.
- [WI-0153](../work-items/WI-0153-video-metadata-collections.md) - metadata and collection integration.
- [WI-0154](../work-items/WI-0154-video-slideshow-playback.md) - mixed-media slideshow playback.
- [WI-0155](../work-items/WI-0155-video-face-processing-evaluation.md) - separately evaluate face processing in video.
- [WI-0156](../work-items/WI-0156-live-photo-pairing-evaluation.md) - separately evaluate iPhone Live Photo pairing.

## Exit criteria

- [ ] M30 is explicitly activated before runtime implementation starts.
- [ ] Supported videos have media kind, duration/capture metadata and poster/proxy support.
- [ ] Videos can be intentionally played without bypassing availability/exclusion/hydration controls.
- [ ] Appropriate manual metadata and collections work across photos/videos.
- [ ] Mixed-media slideshows have deterministic documented behavior.
- [ ] Video face processing and Live Photo pairing receive evidence-based adopt/defer/no-go decisions.
