---
id: M30
title: Video media support
status_source: ../status/milestones.yaml
depends_on: [M19, M22, M24, M27]
---

# M30: Video media support

## Scheduling status

**Architecture gate WI-0150 approved on 2026-10-14.** The maintainer accepted [ADR-0012](../../decisions/ADR-0012-isolated-video-media-architecture.md), the [survey-backed MOV/MP4 H.264/HEVC input candidates](../../operations/video-codec-survey-evidence-2026-10-14.md), and separately installed Winget/Gyan GPLv3+ FFmpeg with provided executable SHA-256 fingerprints. WI-0150 is completed and its activation PR #523 has merged. **WI-0151 is completed following maintainer real-file acceptance on 2026-10-19.** M30 is now `in_progress` with WI-0188 started on 2026-10-19. The durable v37 queue, bounded Windows video tool adapter, revision-fenced publication and photo-priority foundations are implemented. The consolidated Settings-driven photo-first implementation has now passed its first durable real-video publication, restart persistence, original/derivative integrity and photo-feature regression checks on the maintained Windows machine. WI-0188 has since passed operator-reported pause/recovery and local-first/exclusion checks. Controlled invalid-media and scratch-cleanup verification remains; online-only hydration/release has not been exercised and must not be represented as PASS. The completed WI-0151 slice is unchanged, and playback, analysis, retrieval and collection integrations remain proposed. **M30 is not complete.**

## WI-0188 maintained-host progress (2026-10-30)

- **PASS:** first real-video `succeeded` receipt, persistence across
  restart, original/derivative integrity, existing photo features and
  bounded/sanitized queue execution.
- **PASS on maintained host:** Settings/default-off behavior,
  Advance/Pause and recovery/retry, local-first selection and
  included-folder/exclusion handling.
- **Not exercised:** online-only video hydration or app-initiated
  online-only release. All videos exercised so far were already local;
  no cloud-original hydration is claimed.
- **Final controlled verification requested:** unsupported/oversized-media
  rejection and owned scratch cleanup using isolated fixture-based
  Windows tests (runbook), not by modifying the family archive.
  No concurrent workload benchmark or mandatory AppContainer proof
  is being reintroduced.
- Seven historical `attempt-limit` jobs remain untouched; there is no
  reason to reset the queue for closure. M30 still contains later
  proxy, frame analysis, search and playback workitems.

## WI-0188 scope adjustment (maintainer, 2026-10-22)

The maintainer explicitly replaced the original concurrent photo/video
performance test and mandatory AppContainer/security feasibility gate.
WI-0188 now targets a **Settings-enabled, default-off, photo-first** video
phase inside the ordinary Archive Advance/Pause workflow, one video at a
time. Online-only downloads are independently opt-in. Only app-initiated
OneDrive hydration may be reverted to online-only; original media are
never deleted. Manual local functional/recovery/photo-regression acceptance
replaces elaborate cross-workload benchmark rigs.

See [revised WI-0188 acceptance criteria](../work-items/WI-0188-video-isolated-processing-lane.md)
and [consolidated local acceptance runbook](../../operations/video-processing-shared-archive-verification.md).
Existing AppContainer experiments remain optional. Azure fallback for a
video the computer cannot process is deferred; user consent and private
data handling require a separate design and authorization.

## Maintained-host WI-0188 checkpoint acceptance (2026-10-21)

The maintainer confirmed **PASS** for fresh authoritative backup,
isolated restore, guarded **v34→v37** schema rehearsal, unchanged existing
catalogue counts, repeatable schema initialization, and the maintained
v37 PostgreSQL application's health. Video job control remained
**disabled, paused and unselected**.

Maintained regression checks also passed: existing photo Archive,
People/face review, Visual/Caption/Combined search, Smart and Creative
Collections, metadata edits, slideshows, and the existing Videos
library/posters, without unintended OneDrive hydration or changes to
original media. Finally, the optional database-free FFmpeg/FFprobe
synthetic pilot passed with independently validated SDR H.264/AAC
landscape and silent portrait outputs.

These checkpoints are **accepted**; they do not prove real-video
one-shot publication, hard disk/IO/network isolation, cancellation
under workload, or photo-only versus concurrent photo/video
performance. WI-0188 remains `in_progress` until that separate
evidence is collected. No mass video processing or general hosted
video worker has been enabled.

The next safe development checkpoint adds a **read-only metadata-only
one-shot candidate shortlist**, permitting operator selection of an
opaque video revision UUID. It does not open or enqueue media; the
existing exact-revision `video one-shot enqueue` dry run must still
prove local availability before further trial decisions.

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
- [WI-0202](../work-items/WI-0202-photo-catalogue-query-performance-regression.md) — completed photo-only PostgreSQL query regression recovery (formerly blocked WI-0151); [WI-0202](../work-items/WI-0202-photo-catalogue-query-performance-regression.md).
- [WI-0188](../work-items/WI-0188-video-isolated-processing-lane.md) — isolated scheduling/limits.
- [WI-0189](../work-items/WI-0189-video-local-low-resolution-proxies.md) — local playable proxies.
- [WI-0190](../work-items/WI-0190-video-frame-caption-embedding.md) — captions and visual embeddings.
- [WI-0191](../work-items/WI-0191-video-semantic-caption-search.md) — video retrieval.
- [WI-0155](../work-items/WI-0155-video-face-processing-evaluation.md) — face feasibility.
- [WI-0152](../work-items/WI-0152-video-serving-playback.md) — video viewer.
- [WI-0153](../work-items/WI-0153-video-metadata-collections.md) — optional mixed collections.
- [WI-0154](../work-items/WI-0154-video-slideshow-playback.md) — optional mixed playback.
- [WI-0156](../work-items/WI-0156-live-photo-pairing-evaluation.md) — Live Photo evaluation.

## Completed first video slice — WI-0151 local acceptance (2026-10-19)

The maintainer completed all **11/11** consolidated PostgreSQL/schema, opt-in ingest, real-file SDR still-poster, default-off library, photo-isolation and cleanup-preview checks. On the final bounded poster materialization pass, 1,316 candidates were enumerated, one eligible poster was **already present**, zero new posters were needed, seven HDR transfers were deferred and zero changes/failures occurred. The previously generated SDR poster was visually accepted. The backup with isolated restore passed, and no original media or OneDrive placeholder was modified. The severe WI-0202 Archive/Smart Collection regression was independently completed before this final acceptance.

**Scope boundary:** WI-0151 establishes durable, revision-bound, exclusion-aware **Rec.709 SDR still posters and read-only metadata browsing** only. No playable proxy, new video analysis worker, mixed slideshow, manual video annotation, caption/frame embeddings, video search or HDR production posters are approved by this result. The rest of M30's milestone exit criteria remain unchecked until their own work items are implemented and verified.

## Exit criteria

- [x] WI-0150 architecture complete: ADR-0012 accepted, codec/build/hash evidence recorded, and PR #523 merged.
- [x] WI-0151 initial video kind/metadata, revision-bound durable Rec.709 SDR still posters and read-only opt-in Videos library: consolidated maintainer real-file acceptance on 2026-10-19.
- [ ] Metadata/poster, bounded local playback proxy, timestamped captions and embeddings work for a documented codec subset and do not mutate originals.
- [ ] Video processing is opt-in, resource-limited, resumable and does not materially regress photo throughput or interactive behavior under measured local load.
- [ ] Video retrieval is useful and has explicit photo-only compatibility defaults.
- [ ] Face processing gets an evidence-based adopt/defer/no-go and follows existing identity governance.
- [ ] Optional viewer, collections, slideshows and Live Photo integrations are independently verified before introduction.
