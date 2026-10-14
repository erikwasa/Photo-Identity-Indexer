---
id: WI-0190
title: Extract timestamped video-frame captions and visual embeddings
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0151, WI-0188]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Recognition.Onnx, docs]
---

# WI-0190: Extract timestamped video-frame captions and visual embeddings

## Objective

Analyze selected clips through bounded frames/scenes and store versioned visual descriptions and embeddings suitable for later video search.

## In scope

- Compare interval and scene/keyframe sampling; cap decoded duration, sampled frames and frames per scene, including on long videos.
- Reuse local still-image caption/CLIP processing only after verifying preprocessing/model compatibility; no mandatory video-specific foundation model.
- Persist timestamp/interval, frame policy, source revision, model/checksum, preprocessing version, generated caption and vector with provenance and optional conservative per-video summary.
- Evaluate top-k/max similarity versus pooled/deduplicated frame evidence for later clip-level retrieval; report coverage and cost.
- Explain that sampled frames cannot prove motion between frames, speech, sound or precise events; generated descriptions are not verified manual tags.
- Use opt-in isolated jobs with cancellation, idempotency, stale evidence invalidation, privacy exclusion and bounded disk/memory/GPU use.

## Out of scope

Audio transcription, general video action understanding, face recognition (WI-0155), automatic canonical tagging and search UI (WI-0191).

## Acceptance criteria

- [ ] Supported clips produce timestamped capped captions/embeddings or clear unsupported/failure output.
- [ ] Every result is regenerable by revision/model/sampling version and partial/stale jobs cannot appear current.
- [ ] Short, multi-scene, long, rotated and low-light videos demonstrate bounded sampling and documented missed-scene limitations.
- [ ] Record throughput, storage/clip, GPU/CPU cost, and caption/retrieval quality on representative private videos.

## Verification requirements

Sampling, model evidence, failure, privacy and invalidation tests plus local relevance evaluation; no personal media/evidence committed.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
