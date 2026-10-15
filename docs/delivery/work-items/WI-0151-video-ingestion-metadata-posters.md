---
id: WI-0151
title: Ingest supported videos and extract metadata plus poster proxies
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0150]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Source.Local, PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0151: Ingest supported videos and extract metadata plus poster proxies

## Objective

Recognize supported video files as catalogue assets and derive enough metadata/poster imagery for normal library browsing.

## Why

The first useful step is making videos visible/searchable rather than silently unsupported files.

## In scope

- Recognize the activated MP4/MOV subset.
- Persist media kind, duration, dimensions/rotation, frame rate and container/codec/stream details, and available capture time/camera/GPS using documented precedence.
- Generate bounded durable *still* posters/review images tied to source revision; playable lower-resolution video is WI-0189.
- Make initial video discovery opt-in and do not automatically enqueue all discovered videos for analysis.
- Respect OneDrive availability and hydration rules, avoid large-file hydration for cheap enumeration and classify cases requiring local video bytes.
- Report unsupported codecs/containers explicitly.

## Out of scope

- Face recognition across frames.
- Universal transcoding.
- Video job scheduling (WI-0188), smaller playable video cache (WI-0189) and captions/embeddings (WI-0190).
- Live Photo pairing.

## Acceptance criteria

- [ ] Supported videos appear as assets with duration/media kind.
- [ ] A durable poster supports browsing without replaying the original.
- [ ] Date, GPS, duration, rotation, stream metadata and unsupported/unknown fields behave predictably.
- [ ] Default-off video support does not change photo-only ingestion or query results.
- [ ] Unsupported variants remain visible in reporting.
- [ ] Original videos remain read-only.

## Verification requirements

Source, metadata, persistence and representative real-file verification.

## WI-0151 implementation checkpoint 1 — isolated pilot (2026-10-14)

- Initial PR implements separate MOV/MP4 OneDrive discovery, safe opt-in local FFprobe metadata and bounded FFmpeg still posters. It does **not** yet persist these in the PostgreSQL catalogue or make videos visible in the library.
- CLI usage, safety contract and follow-up requirements: [video metadata/poster pilot](../../operations/video-metadata-pilot.md).
- Still required before completion: persisted media-kind/revision metadata and poster lifecycle, default-off app integration, source-copy exclusions/invalidations, gallery browsing, HDR/rotation correctness and tests on real videos.
- Mark WI-0151 **in_progress**, not complete; the first PR is a foundational implementation checkpoint and does not satisfy the acceptance criteria as a whole.

## Maintainer verification and checkpoint 2 — 2026-10-15

- [Sanitized real-video pilot evidence](../../operations/video-pilot-evidence-2026-10-15.md): 25 already-local MOV/MP4 clips probed without failure; 16 H.264/HEVC Main SDR JPEG posters looked correctly oriented, un-stretched and without visible colour defects. The operator observed no unexpected OneDrive downloads. Nine HEVC Main 10 HLG videos were *correctly deferred* pending tested HDR-to-SDR mapping. Longest metadata-probed HLG video was ~152 seconds, **not** a validated long-video poster.
- Checkpoint 2 implements PostgreSQL schema version 33: media-kind discriminator defaults existing assets/revisions to photo, video revision metadata keyed by immutable revision and versioned poster rows with foreign-key cascade. The migration and contract test are isolated from the pilot and insert **no** video records in a live catalogue. Explicit opt-in video scanning/insertion, source-copy exclusions, actual metadata/poster persistence APIs, safe gallery and photo-query isolation remain to be implemented.
- This is a foundation PR and must **not** be treated as end-to-end WI-0151 acceptance. Take a PostgreSQL backup and test migration on a rehearsal database before restarting a maintained production instance.
- Next: secure revision-bound save/query and exclusion filtering, opt-in ingest, deterministic poster invalidation/cleanup and presentation; separately validate HLG colour mapping using selected local clips.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
