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

## Development checkpoint 1 — source-free bounded frame sampling (2026-10-31)

A first opt-in-independent foundation introduces a deterministic
`video-frame-sampling-v1` planner in Core. It produces interval or
scene-balanced seek timestamps from *supplied inspected metadata*, enforces
frame/estimated-decode reservation ceilings and handles short/many-scene/long
clips without opening originals or activating video work. Synthetic unit
tests cover bounds, scene distribution, timestamp monotonicity and malformed
input. See [frame sampling runbook](../../operations/video-frame-evidence-sampling.md).

**This is not a captioning or embedding pipeline.** There is no real frame
decoding, model preprocessing reuse decision, PostgreSQL frame evidence table,
search integration, runtime quota enforcement or maintainer media validation.
All four acceptance criteria remain open. Next work: a strictly opt-in,
locally verified worker-frame extraction and model compatibility pilot,
followed by durable provenance/invalidation and maintained-host quality/cost
verification. No online-only hydration is authorized by this checkpoint.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
