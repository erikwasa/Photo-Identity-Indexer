---
id: WI-0190
title: Extract timestamped video-frame captions and visual embeddings
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0151, WI-0188]
related_adrs: [ADR-0012, ADR-0013]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Recognition.Onnx, docs]
---

# WI-0190: Extract timestamped video-frame captions and visual embeddings

## Objective

Analyze selected clips through bounded frames/scenes and store versioned visual descriptions and embeddings suitable for later video search.

## In scope

- Compare interval and scene/keyframe sampling; cap decoded duration, sampled frames and frames per scene, including on long videos.
- Reuse existing still-image caption/CLIP processing only after verifying preprocessing/model compatibility; no mandatory video-specific foundation model. Local is the default, but ADR-0013 permits a **separately implemented** opt-in operator-managed Azure inference route. Frame resolution, prompt/context, sampling and inference bounds must be evaluated for video, not silently inherited from photo-caption defaults.
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

## Development checkpoint 2 — fixed frame JPEG and uncommitted evidence contract (2026-10-31)

A synthetic-only CLI now uses the existing hash-pinned constrained FFmpeg
supervisor to generate disposable portrait/landscape video fixtures and
extract four interval-sampled frames per fixture. Its fixed SDR JPEG profile
enforces accurate timestamps, rotation handling, output-size and geometry
bounds; frames are decoded using the same still-image BGR24 path as photos.
All outputs are temporary; no original, OneDrive placeholder, PostgreSQL
database, real-video job or model is accessed. CI exercises argument,
authorization-surface and data-contract tests; optional marked-temp Windows
FFmpeg execution is documented in the [frame evidence runbook](../../operations/video-frame-evidence-sampling.md).

A separate **in-memory only** evidence validator now enforces exact frame
count/timestamps, a caller-supplied expected revision/model/sampling/extraction
identity, safe captions, finite same-dimension embeddings and fail-closed
incomplete/excluded/stale decisions. The validator **cannot** authorize a
database commit: production needs a revision/lease/exclusion-fenced
PostgreSQL persistence implementation and actual reuse of verified photo
caption/CLIP preprocessing. Scene detection, HDR/HLG handling and private
quality/cost benchmarks are still required. **0/4 acceptance boxes checked**.

## Development checkpoint 3 — actual local models, registered proxy only (2026-10-31)

An explicit, **read-only** pilot now resolves one already-published current,
unexcluded **SDR video proxy** by its opaque PostgreSQL job UUID; it never
opens originals or hydrates OneDrive. The source-row lock spans snapshot
copying and SHA verification into owned, marked temporary space. A strict
four-frame cap, hash-pinned Windows FFmpeg job processes, existing JPEG
decoder, existing still-photo **CLIP ONNX model/preprocessor and loopback
Ollama caption client** now form a model-powered transient pipeline.
Before/after checks refuse stale/revoked registrations; the in-memory
provenance contract validates both model hashes, caption prompt,
frame positions, vector shapes and completeness.

Optional CLIP text query diagnostics compute max/top-k/pooled cosine
and near-duplicate frame counts, with **numeric aggregate output only**.
The pilot does not write PostgreSQL, register a worker, publish captions,
affect photo search or decide a video-ranking policy. This closes a large
implementation gap toward acceptance criteria 1, 3 and 4, but not
their maintained-host evidence or durable acceptance; criteria **0/4
checked**. See [the registered-proxy pilot runbook](../../operations/video-registered-proxy-frame-pilot.md).

### Checkpoint 3 maintained-host verification and timeout correction (2026-10-31)

The maintainer confirmed the approved FFmpeg 9.0.2 executable, pinned
CLIP model/tokenizer, current local PostgreSQL video proxy, owned scratch
marker and operator-controlled Azure Ollama bridge. The synthetic frame
pilot **passed** with four 640×360 landscape and four 360×640 portrait
JPEGs, and scratch cleanup. The registered-proxy pilot dry-run passed;
the first real 12.3-second registered-proxy attempt extracted a valid
18,065-byte JPEG in 333 ms but terminated before accepting any frame:
`local-caption-and-clip:timeout`. Independently, the Azure bridge
finished the first `POST /api/chat` with HTTP 200 after 121.9 seconds.

The implementation fault was a **single shared 60-second supervisor
deadline for both FFmpeg and its model-inference callback**. The repair
preserves 60 seconds for extraction, adds an independent 180-second
per-frame model-inference deadline within the original 10-minute
global cap, performs another current-revision check before any caption
request, and includes failed inference time in aggregate `modelMs`.
Tests exercise timeout, successful slower-than-decode model work and
overall cancellation without contacting personal media or Azure.

**Maintainer verification update (same day):** With the Azure bridge
already warmed, the *pre-fix checkout* successfully completed a
two-frame registered-proxy run: `status=validated-not-published`,
`frames=2`, `jpegBytes=36213`, `decodingMs=128`,
`modelMs=2551`, `errorCode=null`, exit code 0. Only the original
scratch ownership marker remained; report flags confirm no originals,
OneDrive hydration, jobs, schema changes or evidence persistence.
The previous 121.9-second Azure request and this fast warmed run show
why inference requires a separate deadline. The latter establishes
successful real-video CLIP/caption inference and temporary cleanup on
the maintained Windows host, **not** that the subsequently merged
PR #677's cold-start fix was exercised in that particular run. PR #677
merged with all Windows CI jobs passing. A post-merge `main` run is
recommended to specifically accept the corrected deadline boundary.

This verifies checkpoint 3's manual inference proof without satisfying
the complete WI-0190 requirements: **0/4 formal acceptance boxes remain
checked**. See the [registered-proxy runbook](../../operations/video-registered-proxy-frame-pilot.md).

**Next:** native `FrameEvidence` job registration/execution, versioned
transactional PostgreSQL frame evidence with current-lease/exclusion
and incomplete/stale fences, real-video retrieval quality/cost and
short/multi-scene/long/rotated/low-light benchmarks. New migrations
must be rehearsed on isolated restored PostgreSQL before local install.
Online-only video remains WI-0207.


## Development checkpoint 4 — PostgreSQL v38 atomic frame evidence foundation (2026-10-31)

Added an **append-only v38 migration** with private
`video_frame_evidence_batches` and `video_frame_evidence_frames`.
Batches carry the source revision, sampling/extraction versions, exact CLIP
model hash and preprocessing identity, caption model hash/prompt version,
frame count and embedding dimensions. Each child frame stores its exact
timestamp, optional scene index, bounded caption, and portable
little-endian float32 vector bytes.

A source-copy exclusion INSERT now physically deletes matching private frame
batches (and cascades to their captions/vectors), serialized against the
publisher's source lock. Deletion of a revision also cascades through foreign
keys. Worker cancellation/purge integration and retention auditing remain
future work.

An explicit, **unregistered** `PostgresVideoFrameEvidenceRepository`
publishes only fully validated caption+embedding batches under an existing
`FrameEvidence` job's live lease. Within **one PostgreSQL transaction**
it serializes the source row, operator control generation and job row,
rechecks latest/current local SDR revision, exclusion, cancellation,
lease expiration, photo-priority and exact job/sampling/tool identity,
then commits **job success + batch receipt + every frame row together**.
A failed/partial/forged/revoked publication returns refusal without any
successful job or receipt. Its private read enforces a succeeded matching
job, complete ordered frame count, consistent vector dimensions,
supported current revision and unexcluded source; old revisions remain
invisible to the consumer. The repository does not start workers or
contact FFmpeg, Ollama, OneDrive or the internet.

Isolated disposable PostgreSQL tests cover commit/read round-trip,
wrong lease estimates, incomplete batches, consumed leases, operator
pause and exclusion invisibility. The migration is **not authorization to
run the maintained Windows catalogue**: do a fresh authoritative backup,
verified isolated restore and the newly added guarded `catalogue rehearse-v38`
command against that restore first. CI uses only
ephemeral databases. The normal app still processes photos as before;
this slice adds no API and no automatic `FrameEvidence` worker.

**Progress:** The maintained Windows Azure+CLIP pilot produced two complete
transient frames successfully. v38 now defines and fences their **future
durable storage**. An actual production worker/lease heartbeat, opt-in
queue wiring, cancellation/recovery, end-to-end exclusion/purge orchestration,
searchable current-evidence API, HDR/multi-scene/low-light coverage and quality/cost
evaluation are still open. **0/4 formal WI-0190 acceptance criteria checked**
because no real-video evidence is yet published through this new path.
See [v38 operations gate](../../operations/video-frame-evidence-v38.md).

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
