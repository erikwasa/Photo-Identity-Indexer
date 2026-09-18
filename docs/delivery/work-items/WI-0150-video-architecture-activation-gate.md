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
- Decide poster/proxy/transcode responsibilities and privacy/storage boundaries.
- Define browser range-serving and hydration expectations.
- Define how manual metadata and collections apply.
- Sequence optional face-frame analysis and Live Photo pairing separately.

## Out of scope

- Implementing video while M30 remains held.
- Universal codec promises.
- Making frame-level face recognition prerequisite for basic support.

## Acceptance criteria

- [ ] An accepted design identifies the smallest useful first slice.
- [ ] Media-neutral versus image-specific contracts are explicit.
- [ ] FFmpeg/platform codec dependencies are evaluated before adoption.
- [ ] M30 leaves the intentional hold only by explicit maintainer decision.

## Verification requirements

Architecture review only; no runtime implementation while blocked.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
