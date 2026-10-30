---
id: M30
title: Video media support
status_source: ../status/milestones.yaml
depends_on: [M19, M22, M24, M27]
---

# M30: Video media support

## Scheduling status

**M30 is in progress, not completed.** WI-0150 architecture and WI-0151
video metadata/SDR posters are completed. **WI-0188 completed
following maintainer acceptance on 2026-10-30** for already-local,
default-off, photo-first, bounded video processing and durable
proxy publication. The operator confirmed first real-video success,
restart persistence, original/derivative integrity, Settings and
Pause/recovery, local-first/exclusions, existing photo regressions,
and **17/17 isolated Windows safety tests** for unsupported/oversized
input guards and temporary-file cleanup.

**Online-only video hydration/release has not been exercised.**
The maintainer explicitly moved that separate opted-in OneDrive
verification to [WI-0207](../work-items/WI-0207-opt-in-onedrive-video-hydration-and-release.md),
a new proposed M30 workitem dependent on WI-0188. This does not
permit automatic hydration or alter locally available video behavior.
WI-0189 local proxy cache retention remains **in progress** (PRs #665–#669
merged, PR #670 under review);
other M30 playback, frame analysis, search and collection integrations
remain separately proposed. **M30 is not complete.**

## WI-0189 versioned bounded video proxy profiles (PR #670, 2026-10-30)

PR #670 introduces the second, strictly whitelisted compact **540p**
H.264/AAC SDR software encode profile, retaining the existing exact
720p preset and behavior as default. Video Settings persists the
profile and blocks changes while an attempt is active. Job identity,
pending work and queued work now use the selected preset version
and approved FFmpeg hash: old queued jobs are not processed under
the wrong profile, and old completed proxies are never silently
rewritten or deleted. Independent FFprobe validation bounds output
dimensions to the selected profile; all archive/photo priority,
revision/exclusion/lease and original immutability boundaries remain.

GitHub CI and maintained-host representative media acceptance for
PR #670 are pending. The compact profile is a candidate only; no
claims are made that it achieves better quality/size/performance.
Automatic retention and hard physical disk quota are still absent.

## WI-0189 confirmed, operator-only retention checkpoint 5 (2026-10-30)

PR #669 adds a **manually confirmed**, small-batch proxy cleanup workflow
under Video Settings. It reuses the existing stopped-lane DB-first
registered-derivative maintenance, with independent checks for
disabled+paused controls, no running leases, private cache ownership,
recorded SHA-256 and canonical file path. The dry-run returns aggregate
counts and a five-minute stale-plan fingerprint; confirmation re-evaluates
the exact plan before PostgreSQL revocation and private unlinking.
Existing originals and unrelated cache files are not selected.

This is **not** a background retention service or hard disk quota.
There is no unattended eviction, no original/OneDrive hydration, no
new transcoding profile or real-media quality acceptance in this PR.
GitHub Windows build CI passed for PR #669;
maintainer-host deletion pilot is deferred to the combined WI-0189
acceptance session.

## WI-0189 persisted Video Settings quota checkpoint 4 (2026-10-30)

PR #668 adds optional **user-configured** registered-byte and per-proxy
publication limits to the Videos Settings UI, saved in the existing
application-local preferences file. Existing two-boolean files remain
compatible; null overrides retain launcher configuration. Invalid or
partial values fail closed; changes while video is active require pausing,
and new limits are read for subsequent video attempts. The controls do not
evict, hydrate, open, or modify originals. No new encoding profiles are
enabled. PR #668 GitHub build CI passed; app-UI smoke remains unverified.

## WI-0189 opt-in publication quota checkpoint 3 (2026-10-30)

Merged PR #667 adds a deliberately configured registered-byte publication
fence to the **existing** shared Archive video lane: per-file cap checked
before publication, aggregate receipt bytes checked under a serialized
PostgreSQL success transaction, path-free non-retryable quota diagnostic
and safe known-uncommitted output cleanup. Existing video preferences,
originals, online-only hydration, poster and photo paths are unchanged.
Without BOTH approved config keys, the original WI-0188 behavior applies.

This **does not automatically evict** files, impose a hard physical
filesystem quota, configure the encode profile from Settings or establish
real MOV/MP4 quality/seek/audio acceptance. PR #667's GitHub build
CI completed successfully; optional maintained-host regression remains. WI-0189 remains in progress
with all acceptance checkboxes open.

## WI-0189 retention checkpoint 2 (2026-10-30)

The maintainer merged PR #665 and locally confirmed all **8/8** focused
retention-policy tests passing on Windows after bypassing a NuGet Webcil
conversion permission error via `WasmEnableWebcil=false`; the maintainer
also reports running documentation validation and regeneration.

Merged PR #666 extends this with a **read-only** PostgreSQL-backed registered
proxy inventory/retention preview, safe private-cache evidence checks,
fail-closed incomplete inventory, and a non-mutating CLI command. The
existing limited operator-only revocation maintenance remains unchanged.
No separate maintainer PostgreSQL result was supplied for PR #666. No automatic eviction, persisted quota/profile Settings or
hard filesystem quota is enabled. See the
[WI-0189 retention runbook](../../operations/video-proxy-retention-wi0189.md).

## WI-0189 retention checkpoint (2026-10-30)

PR #665 begins WI-0189 using the existing locally staged/validated
SDR playable-proxy pipeline from WI-0188. It adds a deterministic
**non-deleting** cache-quota planner and focused policy tests, plus
[retention checkpoint documentation](../../operations/video-proxy-retention-wi0189.md).
No automatic eviction, configurable Settings profile, quota wiring,
real-video quality acceptance or browser playback is delivered in this
checkpoint. WI-0189 remains in progress; its exit criteria are unchecked.
OneDrive-only hydration/release remains WI-0207 and is not enabled.

## WI-0188 final maintained-host acceptance (2026-10-30)

- **PASS:** one live video derivative durably published on attempt 1,
  persisted after restart, with original/source and derivative integrity
  verified by the maintainer.
- **PASS:** photo Archive, People, Visual/Caption/Combined search,
  Smart/Creative Collections and slideshow compatibility, and guarded
  photo-first video queue/Settings controls.
- **PASS:** Advance/Pause and interrupted-job recovery/retry; local-first
  selection, current included folders, exclusions and no unrequested
  OneDrive hydration.
- **PASS:** targeted Release test groups 8 + 2 + 4 + 2 + 1 =
  **17 passed, 0 failed, 0 skipped**, covering unsupported/video
  source-budget rejection, output validation and bounded scratch
  cleanup after tool failure, cancellation and timeout.
- **NOT EXERCISED / deferred to WI-0207:** actually hydrating an
  online-only OneDrive video and safely returning only
  app-initiated downloads to online-only. Every tested source was
  already local.
- Seven historical `attempt-limit` rows were not reset.
  Concurrency benchmarks and mandatory AppContainer proof remain
  explicitly out of scope as approved on 2026-10-22.

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
3. **Isolated local video processing — WI-0188 (completed):** default-off, one-at-a-time, photo-first queue for already-local media, with guarded FFmpeg execution, durable derivative publication, cancellation, recovery and observability.
4. **Online-only OneDrive video — WI-0207 (proposed):** explicitly opted-in bounded hydration and safe return-to-online-only, to be verified independently without modifying pre-existing local originals.
5. **Local low-res video copy — WI-0189 (in progress):** managed local transcode/cache independent of immutable originals and OneDrive, with disk quota, eviction, invalidation and configurable output profile.
6. **Video visual evidence — WI-0190:** bounded timestamped frame sampling, caption generation and versioned CLIP-compatible embeddings when model suitability is confirmed; per-video/scene aggregation and uncertainty.
7. **Video retrieval — WI-0191:** explicit Videos/Photos search scope, Visual/Caption/Combined ranking at video level, with meaningful matched timestamps and no changed photo-search defaults.
8. **Face feasibility — WI-0155:** separately evaluate sampled face detection, temporal track deduplication and suggestions against *existing confirmed* identities; review burden, precision and compute/storage decide adopt/defer/no-go.

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
- [WI-0188](../work-items/WI-0188-video-isolated-processing-lane.md) — completed, maintainer-accepted local-video processing, scheduling/limits and safety.
- [WI-0207](../work-items/WI-0207-opt-in-onedrive-video-hydration-and-release.md) — proposed independently opted-in OneDrive-only video hydration and safe release verification.
- [WI-0189](../work-items/WI-0189-video-local-low-resolution-proxies.md) — in-progress local playable proxy retention (PRs #665–#669 merged; #670 under review).
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
- [x] Initial locally available video processing is opt-in, photo-first, one-at-a-time, resource-limited, recoverable and accepted without photo functionality regressions under the maintainer-revised WI-0188 scope. Remote OneDrive-only hydration/release is separately pending as WI-0207; concurrent performance benchmarking was explicitly removed.
- [ ] Video retrieval is useful and has explicit photo-only compatibility defaults.
- [ ] Face processing gets an evidence-based adopt/defer/no-go and follows existing identity governance.
- [ ] Optional viewer, collections, slideshows and Live Photo integrations are independently verified before introduction.
