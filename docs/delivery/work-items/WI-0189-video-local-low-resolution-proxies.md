---
id: WI-0189
title: Generate and retain bounded local low-resolution video proxies
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0151, WI-0188]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0189: Generate and retain bounded local low-resolution video proxies

## Objective

Generate a smaller playable video copy to keep on the local machine, separate from read-only originals and the OneDrive archive.

## In scope

- Opt-in transcoding through the isolated lane after WI-0150 approves FFmpeg/deployment decisions; measure a browser-friendly profile such as bounded 720p H.264/AAC MP4 without assuming universal input compatibility.
- Application-managed **local** derivative cache, never the original or OneDrive-synced folders. Store source revision, encoder/preset version, size, codec and creation status.
- User-configurable profile, disk quota, per-file cap and retention/eviction; safe eviction affects only generated copies.
- Atomic temp-file publication, deduplication, cancellation cleanup, revision/preset invalidation, and exclusion/deletion privacy cleanup.
- Bounded/manual hydration, explicit unsupported/corrupt-file errors and proxy availability after the original returns online-only where policy allows.

## Out of scope

Player/range API (WI-0152), editing/trimming, universal codec support, captioning and facial identification.

## Acceptance criteria

- [ ] Representative MOV/MP4 clips produce smaller, seekable proxies; orientation, aspect and audio policy are correct, and originals remain bit-for-bit unchanged.
- [ ] Repeated jobs reuse a valid revision/preset-matched proxy; changed revisions or policy invalidate correctly.
- [ ] Quota, eviction, stopped jobs, corrupt input, exclusion and OneDrive-online-only cases are deterministic and do not leak temp files.
- [ ] Report proxy size, encode time, browser compatibility and perceived quality to choose a default profile.

## Verification requirements

Integration lifecycle and representative private-file performance/quality tests, including long/rotated/no-audio clips.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
