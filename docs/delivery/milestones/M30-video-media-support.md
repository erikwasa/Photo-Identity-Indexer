---
id: M30
title: Video media support
status_source: ../status/milestones.yaml
depends_on: [M19, M22, M24, M27]
---

# M30: Video media support

## Scheduling status

**Architecture phase activated on 2026-10-14 by maintainer direction to proceed with WI-0150.** M30 is in progress for design review, but **video runtime implementation is not yet approved**. A successful limited [maintained-archive codec survey](../../operations/video-codec-survey-evidence-2026-10-14.md) now grounds proposed ADR-0012's H.264/HEVC MOV/MP4 candidate inputs. [Proposed ADR-0012](../../decisions/ADR-0012-isolated-video-media-architecture.md) still requires exact FFmpeg binary/security/licensing review and maintainer approval before WI-0151 starts. No FFmpeg binaries, video runtime dependencies or photo-behavior changes are introduced by the architecture phase.

## Goal and rollout

Support private family videos on local hardware without modifying originals, expanding online-only hydration unexpectedly, or changing normal photo ingestion, recognition, search, collections and slideshows. The **initial slice is a separate, opt-in video lane**; ordinary photo results remain photo-only.

1. **Architecture gate — WI-0150:** inspect actual MOV/MP4 codecs and metadata; agree supported subset, FFprobe/FFmpeg packaging/security/license decision, media contracts, video toggle, processing budget, locally retained proxy policy, frame sampling, privacy and ADR.
2. **Metadata/poster — WI-0151:** opt-in discovery; video kind, duration, dimensions/rotation, streams/camera/GPS/time where present; bounded still poster. No mass automatic model processing.
3. **Isolation — WI-0188:** opt-in resource-limited queue for video analysis/encoding, with photo-priority, bounded hydration, cancellation, resumption and observability.
4. **Local low-res video copy — WI-0189:** managed local transcode/cache independent of immutable originals and OneDrive, with disk quota, eviction, invalidation and configurable output profile.
5. **Video visual evidence — WI-0190:** bounded timestamped frame sampling, caption generation and versioned CLIP-compatible embeddings when model suitability is confirmed; per-video/scene aggregation and uncertainty.
6. **Video retrieval — WI-0191:** explicit Videos/Photos search scope, Visual/Caption/Combined ranking at video level, with meaningful matched timestamps and no changed photo-search defaults.
7. **Face feasibility — WI-0155:** separately evaluate sampled face detection, temporal track deduplication and suggestions against *existing confirmed* identities; review burden, precision and compute/storage decide adopt/defer/no-go.

**Later optional integrations:** WI-0152 viewer/range playback, WI-0153 manual metadata and mixed-media collections, WI-0154 slideshow playback and WI-0156 Live Photo pairing. They are not prerequisites for the standalone catalogue/analyze/search path.

## Architectural constraints

- Originals remain read-only; stable asset/revision identity, privacy/exclusions, local-first catalogue and bounded OneDrive materialization remain authoritative.
- Videos and photo workers/queries remain separate by default. No heavy video work or mass transcoding occurs from ordinary photo operations.
- Distinguish still posters, smaller *playable video proxies*, frame-derived captions, visual embeddings and face evidence. All derivatives are revision/preset/model/policy-versioned, disposable and invalidated on changes or exclusion.
- Application-owned local cache, never an original or OneDrive sync directory. Limit total quota, per-file bytes, duration/frames, CPU/GPU/IO and parallelism.
- A visual embedding of sampled frames is **not** a full video understanding: motion between frames, speech and sound are out of scope until separately evaluated.
- Video face detection, intra-video tracks and known-person identity matches are different stages; preserve governed assignment policy and never flood photo review with repeat frames.
- Unsupported codecs/containers and online-only sources must produce explicit diagnostic outcomes, not uncontrolled retries or silent skips.
- Test default-off photo compatibility and throughput with competing video work before opt-in promotion.

## Work items

- [WI-0150](../work-items/WI-0150-video-architecture-activation-gate.md) — design and explicit activation.
- [WI-0151](../work-items/WI-0151-video-ingestion-metadata-posters.md) — metadata and still posters.
- [WI-0188](../work-items/WI-0188-video-isolated-processing-lane.md) — isolated scheduling/limits.
- [WI-0189](../work-items/WI-0189-video-local-low-resolution-proxies.md) — local playable proxies.
- [WI-0190](../work-items/WI-0190-video-frame-caption-embedding.md) — captions and visual embeddings.
- [WI-0191](../work-items/WI-0191-video-semantic-caption-search.md) — video retrieval.
- [WI-0155](../work-items/WI-0155-video-face-processing-evaluation.md) — face feasibility.
- [WI-0152](../work-items/WI-0152-video-serving-playback.md) — video viewer.
- [WI-0153](../work-items/WI-0153-video-metadata-collections.md) — optional mixed collections.
- [WI-0154](../work-items/WI-0154-video-slideshow-playback.md) — optional mixed playback.
- [WI-0156](../work-items/WI-0156-live-photo-pairing-evaluation.md) — Live Photo evaluation.

## Exit criteria

- [ ] WI-0150 architecture phase has begun; accept ADR-0012 and confirm private codec/license evidence before runtime implementation.
- [ ] Metadata/poster, bounded local playback proxy, timestamped captions and embeddings work for a documented codec subset and do not mutate originals.
- [ ] Video processing is opt-in, resource-limited, resumable and does not materially regress photo throughput or interactive behavior under measured local load.
- [ ] Video retrieval is useful and has explicit photo-only compatibility defaults.
- [ ] Face processing gets an evidence-based adopt/defer/no-go and follows existing identity governance.
- [ ] Optional viewer, collections, slideshows and Live Photo integrations are independently verified before introduction.
