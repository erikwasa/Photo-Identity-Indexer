---
id: ADR-0012
title: Introduce video through isolated, opt-in revision-bound processing
status: proposed
date: 2026-10-14
supersedes: []
superseded_by: []
---

# ADR-0012: Introduce video through isolated, opt-in revision-bound processing

## Status and decision gate

**Proposed for WI-0150 review, not accepted for runtime implementation.** On 2026-10-14 the maintainer requested work on WI-0150, activating the *architecture phase* of M30. A successful limited, anonymous real-archive [video codec survey](../operations/video-codec-survey-evidence-2026-10-14.md) has now established initial H.264/HEVC MOV/MP4 input candidates. Actual processing work under WI-0151 and later items still requires acceptance of this ADR, verification of the specific FFprobe/FFmpeg binary and its packaging/license strategy, and explicit confirmation of the implementation scope.

## Context

Photo Identity is a local Windows/OneDrive-sync modular monolith with a PostgreSQL source/asset/revision catalogue, original verification, source-locator exclusions, bounded hydration, versioned review-image derivatives and photo-specific analysis and search. Existing `IAssetRevisionStorageDescriptor` exposes revision, asset, source, SHA-256, size and `MediaType` without requiring image decoder types. By contrast, `OneDriveSyncAssetSource` currently enumerates supported *images* only; image processing, generated captions, face occurrences, Smart/Creative collection queries, slideshow rendering and Photo Search have deliberately photo-specific contracts.

Videos differ in probe cost, size, codec support, timeline, transcoding and face repetition. Extending photo workers by merely adding `.mov` to an image extension list would produce incorrect contracts and unexpected source hydration.

This ADR complements [ADR-0007](ADR-0007-permanent-archive-bounded-storage.md) (permanent source and bounded hydration), [ADR-0008](ADR-0008-source-copy-exclusion-and-purge.md) (source-copy exclusion), [ADR-0010](ADR-0010-local-production-execution.md) (local execution) and [ADR-0006](ADR-0006-canonical-auto-assignment.md) (governed person assignments). ADR-0011's remote caption burst does **not** authorize off-host video processing.

## Proposed decision

### 1. Media and catalogue contract

- Add an explicit `Photo` / `Video` media-kind discriminator to source discovery and revision projections, with a persisted migration and tests. Preserve existing `SourceId`, `AssetId`, `AssetRevisionId`, source-locator exclusion, exact content identity, SHA-256 and immutable revision rules. Do not create a second video source identity or reinterpret existing images as videos.
- Existing `MediaType` remains a MIME/content hint, not the sole kind discriminator. Persist a separate revision-bound video metadata projection (container, video/audio codec, codec profile, stream/rotation, display dimensions, duration, frame-rate when meaningful, and capture metadata with source/provenance); allow absent/unknown values.
- Preserve independent source-copy exclusion and missing-versus-excluded semantics. Deletion of source media does not automatically imply exclusion, while exclusion denies access before local derived artifacts are purged.
- Existing photo-only queries, workers, smart filters, API routes, search results, saved photo lists, creative snapshots and slideshows **must remain byte/behavior-compatible by default**. New video discovery is opt-in. Media-kind gating occurs server-side before expensive analysis and ranking.

### 2. First slice and execution boundary

The first deliverable after this gate is **WI-0151: opt-in catalogue metadata and a still poster for supported local MOV/MP4 files**. No face matching, full transcode or slideshow work is required to make a video indexable. WI-0188 then adds a dedicated video queue used by expensive derivatives; implement lightweight ingestion without launching bulk jobs on ordinary photo scans.

The isolated video lane is an application-owned, local worker within the existing modular monolith, not a cloud service or second database. Jobs are explicitly enabled and selected, capped and cancelable. Photo processing/search/slideshow work has priority; video CPU/GPU/IO/hydration and disk use must be separately measurable and pausable. Defaults: no background video work; at most one heavy video job when deliberately enabled until a mixed-load benchmark establishes safe budgets. Persist revision/model/policy job status and support restart, idempotency, cancellation and failed-attempt cleanup.

### 3. Codec and metadata policy

- **Survey-backed candidate inputs (not universal archive coverage):** `.mov` and `.mp4` files containing H.264/AVC Main/High and HEVC/H.265 Main/Main 10. The maintained-archive survey probed 18 of 1,309 enumerated files successfully: 9 H.264, 9 HEVC; AAC on 17, PCM audio on 1; 15 reported -90° display rotation; 1 HEVC Main 10 file reported HLG transfer. Probe stream compatibility rather than trusting extension. Other codecs remain reportable unsupported variants. Successful stream probing does not guarantee successful frame decoding, browser playback or coverage of 1,291 online-only items.
- Prefer an externally invoked, local `ffprobe` adapter for bounded stream/format inspection; `ffmpeg` is a separately selected derivative tool for poster extraction/transcode. Invoke binaries by explicit trusted absolute path with argument-array APIs (never shells or concatenated command lines); disable network protocols, cap process lifetime/streams and surface sanitized failures. Unknown/unavailable tools yield supported errors, not silent photo fallback.
- Do not open online-only placeholders in source enumeration. Metadata discovery from filename/file attributes is distinct from detailed `ffprobe` metadata; probing media bytes can itself hydrate a placeholder, so run it only on confirmed locally available files or through an explicitly authorized managed hydration lease.
- Prefer preserved embedded capture time (with offset if present), then other reliable media creation tags, then user-supplied effective date, with clear provenance and unknown states. Never invent a time from file modified date as if camera capture time; UTC/QuickTime local-time ambiguities must be resolved in WI-0151 with test samples. GPS and camera tag values remain sensitive local catalogue data.
- Browser-playable proxy candidate for later WI-0189: bounded MP4 with H.264/AAC, maximum 720p preserving rotation/aspect, compatible pixel format and seekable layout. This is a *test profile*, subject to licensing, color/HDR tone mapping, orientation, performance and browser verification; unsupported inputs are explicit.

### 4. FFmpeg deployment, security and license decision

Use a locally installed/external `ffprobe`/`ffmpeg` executable selected by configuration. Do **not** bundle binaries or add managed FFmpeg packages automatically under WI-0150. Prior to production use, record upstream build/version, download origin, integrity/hash, enabled codecs, `ffmpeg -buildconf` license flags, Windows updates and reproducible packaging strategy.

FFmpeg is generally LGPL 2.1+ but GPL configuration/components can change obligations; optional external encoders such as libx264 may require GPL-enabled builds. H.264/AAC formats have separate patent/licensing considerations. These are deployment decisions, not cleared merely by choosing an MP4 output. If binaries will be redistributed, review the actual build and accompanying license/notice/source obligations. A specific approved binary/source and its suitability remain pending the WI-0151 pilot.

Reference: https://www.ffmpeg.org/legal.html ; https://ffmpeg.org/ffprobe.html ; https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Formats/Video_codecs .

### 5. Derived file and evidence lifecycle

- Separate still poster, playable proxy and decoded sampling frames. A poster belongs to WI-0151; a playable low-resolution video belongs to WI-0189. Store derived files under the **application-managed local root**, never inside OneDrive or alongside originals. Do not hash/identify a revision from any derivative.
- Reference cache entries by `AssetRevisionId` + derivative kind + exact profile/tool/model/policy versions. Generate via temporary file, verify, atomically publish, reuse idempotently, and invalidate when source, configuration or exclusion changes. Crash-safe purge includes all video derivatives. Disk quotas, LRU/retention and source-online-only behavior belong to WI-0189.
- Visual evidence (WI-0190) samples bounded keyframes/scenes with timestamps. Per-sampled-frame caption and CLIP-compatible embedding evidence includes model hash, preprocessing/sampling version, source revision and timestamp; sparse frame descriptions **do not establish speech, sound, precise actions or unsampled time intervals**. Store clear machine provenance. Do not auto-promote to human tags or identity labels.
- Face feasibility (WI-0155) distinguishes detection, within-clip temporal tracks and suggestion of a known canonical person. Deduplicate adjacent frames; keep suggestions advisory by default. Any future automatic canonical decision requires separate precision/calibration evidence and the ADR-0006 governed policy, never implicit still-photo thresholds.
- Video Search (WI-0191) uses one ranked hit per video, with optional matched timestamp and caption source, isolated opt-in media-type scope. Current photo Visual/Caption/Combined search and its immutable saved result membership remain unchanged. Mixed-media collections/slideshows are later WI-0153/WI-0154 decisions.

### 6. Serving and privacy

A future WI-0152 viewer serves authorized playable proxies through guarded HTTP endpoints supporting range requests (206/416), cancellation and bounded seek/read; do not expose local cache paths or allow arbitrary file serving. Applying exclusion gates before serving, hydration or model retrieval is mandatory. A missing proxy does not automatically hydrate an online-only original.

No personal frame images, identifying tags, embeddings, filenames, source paths or codec-survey raw output are committed. Video processing remains local unless a **new** specific ADR authorizes an exception.

## Alternatives considered

- Extend existing photo ingestion/recognition jobs directly: fewer classes, but confuses image/video eligibility and risks expensive global regressions. **Reject.**
- Always transcode every discovered video: makes playback easier, but may hydrate large OneDrive files, use excessive disk and delay photos. **Reject.**
- One video embedding only (single frame or average): small index but misses multi-scene moments. **Defer** until sampled-frame relevance measurements.
- Full video-action/audio transcription model from day one: potentially richer search but substantial cost/model complexity; not needed for metadata/proxy/frame search. **Defer.**
- Managed framework-only metadata decoding: avoids FFmpeg packaging but inconsistent MOV/HEVC/rotation support across archive inputs. **Keep as a measured fallback**, not assume universal support.
- Cloud processing: violates local-first intent without a new decision and disclosure contract. **Reject for M30.**

## Initial evidence — maintained archive, 2026-10-14

See [sanitized codec survey findings](../operations/video-codec-survey-evidence-2026-10-14.md). Of 74 MOV and 1,235 MP4 enumerated, 3 MOV and 15 MP4 were locally available; all 18 were probed without error. The rest remained online-only and were not opened. The test inputs were short (15 under 15 seconds, 3 under two minutes), so this does not demonstrate long-video throughput or rare-codec coverage. A separate pilot under WI-0151 must validate actual decoded posters and orientation/HDR/range handling. The private aggregate JSON and personal media were not committed.

This evidence supports the *proposed initial input subset* but does **not** complete the FFmpeg security/license or maintainer-acceptance gates.

## Evidence required before acceptance and subsequent runtime work

1. **Initial evidence obtained:** the maintainer ran the [private video codec survey](../operations/video-codec-survey.md) and supplied an anonymous report; the [reviewed aggregate evidence](../operations/video-codec-survey-evidence-2026-10-14.md) establishes an initial MOV/MP4 H.264/HEVC candidate subset. The unsampled 1,291 online-only videos are not assumed to use those codecs. Additional long/rare samples may be collected during the implementation pilot.
2. Select/document an actual FFprobe/FFmpeg build, Windows packaging/update boundary, license posture and restricted input/timeout policy. The format and licensing discussion above is preliminary; no binary has been verified.
3. Maintainer reviews the opt-in first slice and confirms acceptance of the architecture. The request on 2026-10-14 authorizes **design work**, not unattended mass ingestion.
4. Once accepted, mark this ADR accepted and complete WI-0150 with evidence through `PhotoIdentity.Docs`, then allow WI-0151 to enter implementation. Record photo baseline and resource thresholds during WI-0188.
