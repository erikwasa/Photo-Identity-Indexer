---
id: WI-0150
title: Define the video media architecture and explicitly activate deferred implementation
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0137]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, docs]
---

# WI-0150: Define the video media architecture and explicitly activate deferred implementation

## Objective

Before implementation begins, define how video fits asset/revision, metadata, derivative, collection and playback boundaries and record the deliberate activation decision.

## Why

Video is feasible but broader than another image format: codecs, duration, posters, range serving, browser playback, hydration, frame analysis and Live Photos introduce different lifecycle concerns.

## In scope

- Confirm initial containers/codecs from the maintained archive.
- Define media-kind contracts without rewriting stable identity.
- Distinguish still posters from smaller playable video proxies; choose codecs, managed local cache/quota/retention and revision/exclusion cleanup.
- Define opt-in video-only processing jobs, priority relative to photos, bounded CPU/GPU/IO/hydration and stop/resume controls.
- Define sampling/caption/visual embedding evidence, model/provenance and search-media defaults without assuming sparse frames describe full motion/audio.
- Define browser range-serving and hydration expectations.
- Define optional manual metadata/collection integration *later* while preserving existing photo-only results by default.
- Sequence face/track identification experiments, viewer/slideshow work and Live Photo pairing separately.
- Record an accepted ADR, supported archive codec survey and FFprobe/FFmpeg packaging/security/licensing review at activation.

## Out of scope

- Implementing video while M30 remains held.
- Universal codec promises.
- Making frame-level face recognition prerequisite for basic support.

## Acceptance criteria

- [ ] An accepted design identifies the smallest useful first slice.
- [ ] Media-neutral versus image-specific contracts are explicit.
- [ ] FFmpeg/platform codec dependencies, packaging, security and licensing are evaluated before adoption.
- [ ] Standalone opt-in video lane, limits, cache/invalidation and video search scope are documented and reviewed.
- [ ] WI-0188 through WI-0191 are separately sequenced; face matching is not a prerequisite for ingestion, proxies or visual evidence.
- [ ] M30 leaves the intentional hold only by explicit maintainer decision.

## Verification requirements

Architecture review only; no runtime implementation while blocked.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
