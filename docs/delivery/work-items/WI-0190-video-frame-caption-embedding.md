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

## Maintainer verification — checkpoint 8 metadata-only batch audit (2026-10-31)

The maintainer **successfully ran** `video frames audit` for the actual
published FrameEvidence UUID after PR #685 merged. Sanitized result:
`readOnly=true`, `current=true`, `reasonCode=current-complete`,
expected/durable frames **2/2**, embedding dimensions **512**, caption
UTF-8 payload **151 bytes**, vector bytes **4,096**, combined payload
**4,247 bytes**, first/last sampled timestamps **3,075 / 9,225 ms**
within a **12,300 ms** inspected duration. Report flags confirmed no
original opened, no OneDrive hydration, no captions/embeddings returned,
no video job enqueued and no schema change. This is accepted **local
currentness and payload-baseline evidence**, not local relevance,
scene recall, motion/audio claims or CPU/GPU/billing evidence.
No need to repeat this read-only audit solely for checkpoint 8.

## Development checkpoint 9 — read-only human-annotated scene coverage (2026-10-31)

Add `video frames coverage`, a strict **read-only**, local PostgreSQL
v38 audit for one already-published FrameEvidence job. Given numeric
scene boundary timestamps **manually annotated** by an authorized
operator, it reuses checkpoint 8's complete/current/exclusion/revision
predicate **in the same repeatable-read read-only transaction**, reads
only persisted numeric frame ordinals and timestamps and calculates the
number and indices of covered/missed scenes. It also computes purely
hypothetical interval and scene-balanced sampling at the **same frame
count** to make sampling trade-offs measurable; none of these simulations
publish, decode, or replace the original frames.

The policy is pure and bounded: 1–24 stored frames, up to 128 strictly
increasing scene cuts, valid clip duration, seek positions and
scene-start boundary assignment; coverage reports are numeric only.
Synthetic tests cover malformed inputs, short/long clips and missed
scenes, and disposable PostgreSQL tests cover a current batch and
fail-closed stale/excluded batches.
The existing FrameEvidence worker's disposable PostgreSQL integration
fixture now additionally induces an in-flight cancellation and a new
photo sync request during production, confirming that neither can create
a published batch. These are synthetic, not maintained-host interruption
or measured FFmpeg/job-object shutdown evidence. The maintainer's exact real-video
annotation and multi-clip scene-recall/quality evaluation **remain
local verification**, not synthetic CI certification. See
[scene coverage operations](../../operations/video-frame-scene-coverage.md).

**No automatic scene detection, scene-aware actual frame extraction,
unattended worker, OneDrive hydration, Azure dispatch, new schema or
new production search ranking** is introduced. The selected one-shot
still caps at two interval samples. This provides a measurement
instrument toward criterion 3, but **all four full acceptance criteria
remain open** until varied maintained-host samples, retrieval quality,
resource costs and disruption checks are evidenced. Checkpoint 8's
successful audit does not change WI-0190's `in_progress`/0-of-4 formal
acceptance state.

## Development checkpoint 10 — scene-balanced real FrameEvidence with regenerable annotations (2026-11-01)

After merged PR #687, checkpoint 9 provides a successful bounded
read-only human-annotated scene coverage measurement instrument and
synthetic in-flight cancellation/photo-priority regression tests.
Checkpoint 8's previously accepted maintained-host two-frame audit
remains authoritative. These tests do **not** establish actual scene
quality or GPU cost.

The explicit opt-in `video frames selected-one-shot` is extended
with optional `--scene-boundaries-ms`, accepting 1–6 strictly increasing
integer cut positions from a **private human annotation**. The default
is still two-frame-or-less interval sampling. For scene mode, a new
Core `VideoFrameSceneSamplingIdentity` encodes the **exact annotated
cuts and frame count** in a bounded private PostgreSQL job policy
(`video-frame-sampling-scene-v1-n2-3000_6000_9000` is illustrative).
This permits deterministic regeneration **without** a v39 migration
or a forgotten private sidecar, and differentiates annotations
and selected frame counts from existing interval job idempotency.

The scene-enabled concrete worker strictly recognizes the leased policy,
regenerates its scene-balanced seek timestamps from the inspected
already-current locally published SDR proxy, and retains the existing
Windows-contained FFmpeg, pinned CLIP, explicit loopback caption
disclosure, cancellation, lease/source, exclusion and photo-priority
checks. Its complete per-frame captions/vectors and **scene indexes**
are atomically published through the unchanged v38 boundary. The
standalone read-only coverage command and audit can inspect the
result without reading its private captions or vectors.

New Core tests validate canonical policy/round-trip, distinct identity,
invalid/out-of-duration and too-close plans. Selected one-shot
integration tests enforce preview no-write and invalid annotation
refusal before media/model access. Disposable PostgreSQL worker
tests publish two different synthetic scene annotation batches on
the same revision with distinct jobs and retained scene-indexed rows.
The new CLI flag is not authorization to process originals or hydrate
online-only OneDrive files; **every** real execution still requires
explicit apply and both operator acknowledgements, one selected
already-published local proxy, and the existing protected local
processing environment. See
[checkpoint 10 runbook](../../operations/video-frame-evidence-annotated-one-shot.md).

**Remaining requirements**: real diverse representative clip sampling
(short, long, many-scene, rotated, low-light), automatic scene detection
if desired beyond manual annotations, caption and retrieval relevance
quality assessment, per-clip CPU/GPU/Azure/throughput/storage costs,
and maintained-Windows cancellation/subprocess interruption and
cleanup evidence. Scene-based current production still has a
**two-frame cap**, so this adds a real alternative rather than
acceptance-level coverage of all scenes. The work item remains
`in_progress` and **0/4 complete formal acceptance**, while
several component-level safety and persistence checkpoints are
successfully verified. No new local media run is required merely
for PR review; defer costly local tests to a consolidated acceptance.

## Development checkpoint 11 — offline CLIP retrieval quality gate (2026-11-01)

After merged PR #689, both the approved interval two-frame real-video
batch and the human-annotated scene-balanced production mode are
represented in the current branch. **Only the prior interval happy path
and batch audit were locally accepted**; actual annotated-scene quality
and broad multi-clip throughput are still open.

This checkpoint introduces `VideoFrameRetrievalEvaluation`, a pure
bounded in-memory evaluator of the **already existing** max-cosine,
top-k mean-cosine and deduplicated-pooled CLIP retrieval diagnostics.
It evaluates each policy against the **same manually judged relevant
clip identities**, reporting corpus-aggregate Hit@K, MRR@K, Recall@K,
MAP@K and nDCG@K, with deterministic UUID ties and six-decimal metrics.
It requires explicit matching finite nonzero vectors and complete,
unique, corpus-internal relevance identifiers and validates a
self-declared, consistent CLIP model fingerprint and preprocessing
version. The manifest contract allows at most 32 clips, 32 queries,
24 frames per clip, 2048 dimensions and a 16MiB input file.
No production ranking policy is selected or activated.

A new strictly offline `video frames evaluate-retrieval` CLI reads
one operator-supplied **private local JSON manifest**, reports
**numeric aggregates only**, and refuses UNC/reparse-point inputs,
malformed or oversized JSON, unrecognized keys, invalid vectors or
unjudged references. It never reads video originals, proxies,
PostgreSQL, captions, inference models or Azure, and does not print
private query/clip UUIDs, embeddings, filenames, model digests or
manifest paths. Tests exercise a dataset where the three scoring
policies disagree, correctness of top-1/top-2 ranking metrics, fixed
tie behavior, malformed vector and provenance inputs, and safe
redaction on invalid JSON. See the
[checkpoint 11 runbook](../../operations/video-frame-retrieval-evaluation.md).

**Scope honesty:** The manifest's model provenance is **operator
self-reported**; this command neither generates text vectors from the
approved CLIP model nor securely loads current vector batches from
local PostgreSQL. Real quality assessment still needs a reviewed
**read-only live-catalogue** evaluator to eliminate manual vector
export, verified text/image model compatibility, representative
human judgments, diverse scene coverage and CPU/GPU/Azure cost
instrumentation. This development adds the reusable evaluation and
privacy gate, **not** verified live relevance, and WI-0190 remains
`in_progress`, **0/4 full acceptance**. No local media work or
repeating the accepted two-frame audit is needed to review this PR.

## Development checkpoint 12 — local-current frame relevance without vector exports (2026-11-01)

After merged PR #690, the checkpoint 11 offline private-vector
evaluation instrument is in main and Windows CI passed. This checkpoint
adds the missing **private current-v38 PostgreSQL** data path:
`video frames evaluate-live` consumes an operator-provided private
JSON file containing only selected FrameEvidence **job UUIDs**, bounded
text queries and manually judged relevant job UUIDs. It generates
compatible text embeddings with the reviewed **local CLIP ONNX dual
encoder**, explicitly SHA-256 pins the model, tokenizer vocabulary and
merges, and requires the stored frames' provenance to match that
verified model ID/digest and image preprocessing version.

In one loopback **repeatable-read, read-only** transaction, it
validates current succeeded full v38 frame batches for different
video assets (same gates as the previously successful read-only
audit: latest SDR, local source, not excluded, ordered frames, valid
lengths and timestamps). It selects only stored embedding bytes into
private memory, not captions, original file paths or media. Any
missing/stale/excluded/unsupported/cross-model/duplicate-source
batch fails the whole study without partial results. It reuses the
existing three WI-0190 scoring policies and checkpoint 11 corpus
Hit@K, MRR@K, Recall@K, MAP@K, nDCG@K calculation, returning **only
aggregate numeric metrics** and wall-time components for pinning,
local text encoding, snapshot reads and evaluation. It neither
migrates the schema nor creates video jobs, calls FFmpeg, Ollama or
Azure or introduces an application search API.

The new CLI uses strict absolute physical local-file validation
(including refusing Windows UNC/reparse traversal), bounded manifest
and vector counts, and sanitizes private input and database failure
exceptions to avoid echoing content or source paths. Core human-
judgment validation and disposable PostgreSQL integration regressions
cover valid current batches, model digest mismatch, unsupported and
excluded sources, numeric comparison and invalid labels. The
[checkpoint 12 runbook](../../operations/video-frame-live-retrieval-evaluation.md)
explains safe setup and the required human evaluation.

**Important limitations:** v38 does not store a tokenizer digest,
so token file approval is operator-reviewed and locally pinned, not
independently proved by the catalogue. Snapshot correctness means
current *at the read transaction*, not forever. The existing maintained
host has **one** accepted frame-evidence job; this real multi-video
retrieval study requires at least **two different real videos** with
current evidence and independent judgments, so it is **not** yet
locally verified. No new media run is needed for this PR review,
and do not repeat checkpoint 8's accepted audit. Representative
sampling/missed-scene, CPU/GPU/real Azure cost, and maintained-host
interruption/recovery acceptance still remain. Formal WI-0190
status is `in_progress` and **0/4 full acceptance**.

## Development checkpoint 13 — representative current-frame evidence without media disclosure (2026-11-01)

After merged PR #691, the maintained-host audit's completeness gate
is now reusable in an opt-in private live-catalogue CLIP relevance
evaluator, but **real multiple-clip quality evidence is not yet
collected**. This checkpoint adds `video frames acceptance-report`,
a strict **read-only** comparison instrument for a bounded collection
of up to 32 distinct current FrameEvidence video assets. An
operator-only private local JSON manifest supplies fixed representative
labels (short, multi-scene, long, rotated, low-light), optional
human-annotated scene cuts and optional **measured** per-clip
wall/CPU/GPU/Azure costs and manually judged caption correctness.

A single PostgreSQL `REPEATABLE READ, READ ONLY` transaction applies
the already-rehearsed frame-audit's supported-SDR, current-latest-
revision, source-exclusion, complete frames, valid timestamps,
embedding-length and succeeded-job checks **for every clip**, before
reporting **any** aggregates. It refuses multiple jobs for the same
underlying asset, which would otherwise artificially inflate
representative coverage. Only numeric metadata, aggregate frame
timestamps and private job identities are read; no source path,
original, stored caption text or embedding bytes, CLIP model, FFmpeg,
Azure or OneDrive hydration is accessed. A private annotation is
checked against the inspected duration and the actual durable
timestamps in the same snapshot via existing scene-coverage policy.

The report contains numeric **caption/vector payload bytes** per
current clip (mean and total, excluding PG overhead and proxies),
minimum/maximum clip duration, frame counts, category frequency
and **missing** categories, annotated scene weighted coverage,
observed throughput only for wall-timed clips, and *counts* of
CPU/GPU/Azure cost data actually supplied. Null measurements
remain unknown, never zero. Human caption judgments are likewise
optional and explicitly self-reported. No raw UUID, scene-cut
positions, resource private manifest, caption or vector is emitted.
The report **always says `fullAcceptanceEstablished=false`**.

New Core pure tests check coverage weighting, correct denominators,
missing-data treatment, category gaps, duplication and impossible
human/CPU data. Disposable PostgreSQL tests verify read-only current
reporting, stale revision and source-exclusion refusal, sanitized
input handling and no partial results. Full details in the
[checkpoint 13 runbook](../../operations/video-frame-acceptance-evidence-report.md).

**Scope:** The tool aggregates evidence; it does **not** independently
measure CPU/GPU, determine real Azure costs, detect scene changes,
verify human category accuracy, inspect captions or generate additional
FrameEvidence jobs. The existing one-job maintained-host audit is
accepted but does not represent multiple video types. Run an optional
**consolidated** quality/cost/scene study on real private videos after
the development series; real Windows interruption remains separately
required. Formal WI-0190 stays `in_progress`, **0/4 full criteria
accepted**, and **no new local video work is needed merely to review
this PR**.

## Development checkpoint 14 — crash-safe owned temporary workspace recovery (2026-11-01)

After PR #692 merged, the read-only representative acceptance report
combines actual current v38 frame evidence with optional operator
scene/caption/relevance/resource measurements; it does not close real
Windows interruption acceptance. The registered-proxy frame producer
already normally removes its random private work folder in a
`finally` block, but an abnormal process exit/power loss does
not run `finally`, leaving a nonempty scratch root that correctly
prevents a new selected one-shot.

This checkpoint adds **explicit opt-in, local-only crash scratch
recovery**. Newly created frame workspaces now have an exact child
ownership marker plus a live **OS-exclusive file lock** for their
entire processing attempt. A new `video frames scratch-recover`
command validates the same previously approved OS-temp-only root
and its root marker, discovers only exact GUID-named, marker-owned
child workspaces, checks a hard age threshold (6–720 hours, 24h
default), verifies every file recursively against reparse links,
a 256-entry cap and 320-MiB byte cap, and refuses a still-live
workspace lock. It performs a **dry-run by default** and does
not delete even a valid orphan unless the operator supplies
`--apply --acknowledge-private-scratch-deletion`. Unknown/unmarked
siblings block all deletion. Numeric output never reveals private
names, source paths or model inputs. The production workspace
remains exclusively local; no database, Azure, FFmpeg, media, model,
hydration or job work occurs during recovery.

The supervised FFmpeg temporary child is now nested inside that same held workspace, so an abnormal exit cannot leave unmarked FFmpeg sibling directories at the scratch root.\n\nNormal success/cancel still closes the lock then cleans its child
workspace. A hard crash leaves an aged, unlocked, owned orphan that
can be safely reclaimed **manually** without authorizing a new
video run. Synthetic filesystem tests cover active-lock refusal,
premature age, unknown/unmarked entry blocking, symlink descendant
refusal, explicit apply, post-cleanup ownership and numeric redaction.
See the [checkpoint 14 recovery runbook](../../operations/video-frame-scratch-crash-recovery.md).

**Limitations:** legacy scratch folders from a build without the
child marker are *not* automatically deleted; recovery does not
purge the DB job, replace v38 lease-expiry fencing or validate an
actual FFmpeg/Ollama process kill. The prior maintained-host
two-frame success remains accepted and need not be repeated to
review this PR. A **separate real Windows interruption and
cleanup test on approved disposable private media** is still
necessary for formal acceptance. WI-0190 stays `in_progress`,
**0/4 complete formal criteria**; other open gates include
representative five-class clips, caption/retrieval relevance and
CPU/GPU/Azure costs.

## Development checkpoint 15 — four-frame selected real scene coverage (2026-11-01)

After merged PR #694, opt-in frame scratch workspaces have marker
and OS-held lock recovery for abnormal termination; the remaining
representative-scene bottleneck was the CLI's **two-frame** limit,
even though the source-independent scene-policy and transactional
v38 producer already supported **up to four**.

This checkpoint expands only the explicitly operator-selected
`video frames selected-one-shot` CLI and its admission budget to
**1–4 frames**. Interval mode and private human-annotated scene mode
both reconstruct the exact plan; four requested frames must yield
four valid distinct timestamps before a dry-run is admitted.
The runner now independently rejects any manually constructed
out-of-range frame count before touching its local database or
approved scratch. The scene-policy identity, tool digest, revision
and preset remain in the existing deterministic job key; the
two-frame published jobs are unchanged.

No safety window is expanded: one selected job, 320-MiB scratch,
13-minute global attempt, 15-minute expiring authorization, 60s
FFmpeg and 180s model per frame, photo priority, source-revision/
privacy-exclusion fencing, local pre-published proxy only, no
OneDrive hydration, automatic workers or third-party black-box
processing. Four slow sequential frames may exceed the unchanged
13-minute total and must then **fail without any partial v38
publication**, rather than extending operator authorization.

The optional capacity is covered by CLI parsing tests (four
accepted, five refused, two preserved), disposable PostgreSQL
preflight tests (four distinct annotated scenes admitted without
writes, impossible short scenes rejected and out-of-range
programmatic requests refused), and synthetic PostgreSQL worker
tests (four durable scene-indexed rows with regenerable provenance
versus three-of-four partial publication refusal). New
[four-scene runbook](../../operations/video-frame-four-scene-selected-one-shot.md)
documents a safe read-only Windows preview and the continued
13/15-minute time-budget trade-off. Existing local two-frame
success remains accepted; no repeat is necessary to review
this PR. No real four-scene Azure/FFmpeg evidence has yet been
collected; diverse representative clips, resource/caption/retrieval
quality and real interruption are pending. WI-0190 remains
`in_progress`, **0/4 full acceptance criteria**.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
