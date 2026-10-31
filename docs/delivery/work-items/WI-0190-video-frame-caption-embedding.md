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

## Maintainer local database verification — accepted evidence (2026-10-31)

The maintainer explicitly reported **successful local database migration
verification** for checkpoint 4's v37→v38 gate, following the isolated
backup/restore rehearsal instructions. The precise database counts and
individual command output were not supplied with this report, so they are
**not** invented here. This confirms the operator's local migration
checkpoint has passed; it does not by itself certify any real-video
FrameEvidence publication, worker activation or private-media quality.
No need to repeat the Azure/CLIP pilot solely for this migration milestone.

## Development checkpoint 5 — lease-monitored frame evidence worker boundary (2026-10-31)

The existing video queue and one-shot coordinator now have a **derivative-kind
claim filter** so a FrameEvidence adapter never accidentally claims an
older PlayableProxy or FaceEvidence job. The existing approved playable-proxy
adapters explicitly declare PlayableProxy; existing synthetic coordinator
tests retain their legacy generic adapter behavior.

A new **unregistered** `VideoFrameEvidenceWorkerAdapter` accepts a
complete timestamped frame batch from a separate, future reviewed
`IVideoFrameEvidenceProducer`. For an already-claimed bounded FrameEvidence
job, it checks lease/source/control/photo-priority before production and
before commit, renews the lease on a bounded timer, propagates cancellation,
runs exact plan/revision/frame-count validation and invokes v38's atomic
PostgreSQL publisher. Monitoring is awaited on every outcome. The coordinator
now verifies the durable **FrameEvidence batch receipt** and completed job,
rather than incorrectly requiring an MP4 playable-proxy receipt or believing
an adapter's in-memory success claim.

Disposable PostgreSQL integration tests supply only synthetic in-memory
captions/vectors, and cover correct-kind selection with an older proxy job
queued, successful atomic publication, incomplete-batch denial, forged
success denial, operator pause, and physical exclusion purge. No personal
media, model server, cloud access, FFmpeg, original file, online-only
hydration, web API or unattended worker is involved.

**What is not implemented or authorized:** a concrete media producer
that securely stages a registered video proxy and uses supervised FFmpeg,
CLIP and optional operator-controlled Azure captions while honoring live
lease cancellation. The project continues to keep real-video one-shot CLI
execution blocked by its independent media-isolation gate. This checkpoint
adds the reusable **job/persistence** half, not activation. Next work should
build and independently validate a bounded producer using the already
verified read-only registered-proxy pilot, then connect it only under
explicitly approved opt-in operator control.

**Formal WI-0190 status remains in progress, 0/4 full acceptance.** The
maintained-host v38 migration gate and transient Azure/CLIP pilot are
verified; end-to-end persistent *real-video* frame evidence and full
representative-media evaluation are still required. See
[checkpoint 5 operations](../../operations/video-frame-evidence-worker-boundary.md).

## Development checkpoint 6 — concrete registered-proxy frame producer (2026-10-31)

Added an **unregistered concrete** `VideoRegisteredProxyFrameEvidenceProducer`
implementing checkpoint 5's `IVideoFrameEvidenceProducer`. A valid claimed
FrameEvidence lease must exactly match the pinned FFmpeg SHA-256,
`video-frame-sampling-v1` sampling identity, the fixed
`wi0190-sdr-frame-jpeg-v1` extraction profile and a 1–4 frame limit.

The producer resolves an existing, current **PlayableProxy receipt by
exact leased video revision**, not a source file path. It uses the existing
source-locked and SHA-verified local proxy snapshot copier; originals and
OneDrive placeholders are never opened or hydrated. A separately
operator-created, marker-owned and initially empty root immediately below
OS temp holds a unique private workspace. The producer reserves sufficient
free space for a bounded 256 MiB proxy and extraction scratch, rejects linked
and overlapping directories, and cleans up its own workspace on every
ordinary completion/failure.

Every frame uses the existing fixed 640-pixel SDR extraction with a
60-second Windows Job Object process budget, followed by the already
verified still-photo BGR24/CLIP ONNX embedding path. Ollama caption
inference is separately bounded to 180 seconds per frame, reachable only
through loopback. It may be routed to **operator-controlled Azure** by an
explicit operator-managed bridge under ADR-0013; the producer has **no**
configurable third-party processing endpoint. A new versioned
`wi0190-visible-video-frame-v1` prompt describes only directly visible
content and explicitly disallows inference of motion between frames, audio,
speech, people identities or events. The original still-photo caption
prompt remains unchanged.

The producer rechecks the live job lease/photo priority before staging,
each extraction, each image disclosure and return, and rechecks both the
registered proxy and Ollama model digest before returning the complete
in-memory batch. It does **not** write PostgreSQL itself: checkpoint 5's
adapter and v38 fenced transactional publisher remain the sole commit
boundary. No API endpoint, scheduled worker, CLI activation, job enqueue,
schema v39 or automatic cloud dispatch is introduced.

CI tests check isolated scratch ownership/refusal, invalid model/tool
configuration, a forged lease's early refusal, and PostgreSQL selection
of exact current proxy receipts with source-copy exclusions. Loopback
HTTP tests confirm the distinct versioned video prompt without real
personal media. Real-model execution and durable real-media publication
**remain subject to the future explicitly authorized one-shot integration
verification**. The separate media-isolation gate in the existing
video one-shot command has not been bypassed.

**Progress:** The previously verified real-video transient Azure/CLIP pilot,
successful maintained Windows v37→v38 migration rehearsal, and now the
concrete producer/lease/persistence layers cover the technical building
blocks of acceptance criterion 1 and much of criterion 2. However, no
operator-facing safe integration workflow has yet demonstrated real-video
durable publication; benchmark coverage, HDR handling, sampled-scene
limitations, storage/clip, mixed photo load and retrieval quality remain.
**Formal WI-0190 acceptance stays 0/4**. See the
[checkpoint 6 runbook](../../operations/video-registered-proxy-frame-producer.md).

## Development checkpoint 7 — operator-selected durable FrameEvidence one-shot (2026-10-31)

An explicit, **default-dry-run** `video frames selected-one-shot` CLI
now joins the already-verified local playable-proxy producer with the
checkpoint 5 lease-monitored adapter and v38 atomic PostgreSQL
publisher. The operator selects a **current PlayableProxy job UUID**
and explicitly supplies a local cache, dedicated owned empty frame
scratch, physically pinned FFmpeg, CLIP files and approved SHA-256
digests for **both** CLIP and Ollama; no original file path or arbitrary
model server URL is accepted. The independent still-photo processing
and older original-video one-shot media-isolation hard stop remain
unchanged.

A read-only preflight verifies authoritative schema v38, exact current
published proxy, supported SDR revision, local cache/scratch ownership,
FFmpeg digest, quiet photo queue and disabled/paused/unselected video
control. It cannot queue a job, arm control or disclose image pixels.
The sole execution path requires `--apply`, explicit
`--acknowledge-isolated-v38-rehearsal` and
`--acknowledge-frame-disclosure`. It enqueues one idempotent
`FrameEvidence` job for the proxy's exact revision and runs a single
at-most-**two-frame** attempt. The v37 selected-job grant was extended
to `FrameEvidence` **only when the caller explicitly selects that
derivative kind**, preserving PlayableProxy default behavior. The
15-minute absolute PostgreSQL grant, 13-minute attempt, job lease
heartbeat, photo-priority veto and independent per-frame decoder/model
budgets remain in force. Disarm runs under a separate bounded cleanup
deadline even when the attempt fails. No automatic retry or hosted
worker is enabled.

CI tests cover default dry-run option parsing, refusal of arbitrary
source paths, mandatory distinct acknowledgements, marker isolation,
and SQL-serialized exact `FrameEvidence` grant/claim/disarm without
widening the legacy proxy grant. The selected command reports only
sanitized status and never returns captions, identities, source paths
or embeddings in JSON.

**Maintainer acceptance now required** after all CI jobs pass and PR
merges: the already verified current proxy and operator-managed Azure
bridge can be used to run the read-only preflight, then **one
explicitly authorized two-frame publication**. Verify durable frame
count and embedding dimensions in v38, photo/lease control restored,
the original scratch marker alone remains and no unexpected hydration
or original-file accesses occur. Runbook:
[checkpoint 7 selected one-shot](../../operations/video-frame-evidence-selected-one-shot.md).
Separate local disruption/exclusion/oversized-media tests and
representative-video benchmark still remain. Do **not** mark WI-0190
accepted solely on a two-frame successful publication. **0/4 formal
criteria accepted** until end-to-end and quality gates are fulfilled.

## Maintainer acceptance — checkpoint 7 durable real-video evidence (2026-10-31)

**Checkpoint 7's maintained-host happy-path verification is accepted.**
The maintainer reported the following sanitized Windows results; no personal
media or caption text is included in the repository:

- A previously successful isolated v37-to-v38 migration rehearsal preceded
  the maintained-catalogue deployment. Starting the current application
  upgraded the authoritative photoidentity_main catalogue to **schema 38**,
  with both FrameEvidence tables present. The app was stopped again before
  the one-shot verification.
- Video controls were disabled, paused and unselected; all observed photo
  priority and active video-job flags were false. Read-only selected-proxy
  preflight returned dryRun=true, admitted=true, reasonCode=preflight-only
  and exit **0**.
- The operator-managed **Azure Ollama bridge on loopback port 11434**
  successfully served the approved qwen2.5vl:3b model digest. Explicit
  one-shot apply with both disclosure and rehearsal acknowledgments returned
  dryRun=false, admitted=true, published=true, reasonCode=published,
  originalsOpened=false and oneDriveHydrationRequested=false, exit **0**.
- A private PostgreSQL aggregate showed **FrameEvidence / succeeded**,
  frame_count=2, durable_frames=2 and embedding_dimensions=512. The
  scratch directory contained only its original ownership marker.
  Afterwards the video control was disabled/paused/unselected and the four
  observed photo/video activity flags were false. The maintainer reported
  stopping the Azure bridge and scaling down the Container App.
- Initial local setup encountered the recurring preflight error
  "Approved video tool is missing or linked": the installed FFmpeg
  command was exposed through a WinGet/reparse-point alias. Using the
  physical approved executable passed the final read-only preflight. The
  [pinned FFmpeg/FFprobe operations guide](../../operations/ffmpeg-approved-video-tools.md)
  and shared read-only launcher helper now prevent runbooks from
  rediscovering linked commands through PATH.

**Scope limitation:** This is one selected **already-local SDR proxy's**
two-frame happy path, durable persistence, normal scratch cleanup and restored
controls. It does not demonstrate cancellation under disruption, photo
preemption, stale/excluded revision behavior, oversized/unsupported rejection,
representative short/multi-scene/long/rotated/low-light coverage, retrieval
quality or resource costs. Those acceptance gates still need development
and local verification; no automatic worker or Azure fallback was enabled.

**WI-0190 stays in_progress with 0/4 full acceptance criteria checked.**
Checkpoint 7 is accepted; the work item is **not** complete.

## Development checkpoint 8 — read-only frame evidence audit and bounded cost baseline (2026-10-31)

Added a strict, **read-only** `video frames audit` CLI requiring the opaque
FrameEvidence **job UUID** (not the original/source path or PlayableProxy UUID).
Against an already-current local PostgreSQL v38 catalogue it uses a repeatable-
read, read-only transaction. Server-side metadata queries report currentness,
source exclusion, latest revision, supported SDR inspection, succeeded job,
complete ordered timestamps, vector lengths, frame count, caption/vector
**byte counts only**, and a sanitized reason code. No source paths,
originals, OneDrive, FFmpeg, network inference, captions or embeddings are
read or returned; no migration or queue/control write is possible. This
gives the operator a repeatable way to prove a successful two-frame batch is
still current and to measure a partial per-clip storage payload baseline.

The explicit selected-one-shot's JSON now includes a numeric
`executionWallMs` for the attempted worker cycle plus disarm so the
maintainer can begin recording throughput across representative inputs.
This is **not** GPU/CPU consumption or billed Azure cost. The batch audit
counts caption/vector payload only and excludes PostgreSQL page/index/WAL
overhead and registered proxy storage.

Parser/fail-closed unit tests and disposable PostgreSQL integration tests
cover valid two-frame evidence, a newer revision making evidence stale, and
source-copy exclusion physically purging evidence. The preflight fixture
additionally checks unsupported inspection, oversized published proxy and
excluded source refusal, without staging any file or creating a video job. These test cases are
synthetic and have **not** been run on the maintainer's environment.
[Checkpoint 8 operator procedure](../../operations/video-frame-evidence-audit.md).

**Next development/local gates:** actual cancellation/photo-preemption/
disarm-under-failure regression, unsupported and oversized rejection,
short/multi-scene/long/rotated/low-light sampling benchmark and human
relevance judgment, and measured CPU/GPU/throughput/real storage costs.
Two-frame interval sampling remains intentionally limited. The new audit
does not activate unattended workers, implement scene detection or
authorize Azure inference. WI-0190 remains `in_progress` and formal
acceptance remains **0/4**.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
