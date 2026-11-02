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

## Configuration checkpoint — centralized approved inference (2026-11-01)

A cross-cutting configuration review confirmed that the existing launcher
blocked caption and semantic model inputs already consumed by the API.
The proposed reviewed change adds a fail-closed optional caption digest pin,
CLIP model/vocabulary/merges pins, a read-only PowerShell resolver, an
explicit loopback Ollama origin for operator-only video CLI commands, and a
separate non-exported Azure upstream URL. This is **configuration plumbing**,
not permission for remote inference, a new video worker, or proof of the
remaining private-video acceptance criteria. The previously verified
one-shot evidence remains the accepted checkpoint, subject to the
[approved inference configuration](../../operations/approved-inference-configuration.md)
Windows validation procedure before activating newly pinned settings.

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
- [ ] On the currently admitted local SDR proxy cohort, demonstrate bounded sampling and missed-scene limitations with available short/rotated clips and any representative multi-scene, low-light, longer or landscape footage that can be identified. Mark unavailable or not-yet-supported representative categories **postponed**, with a reason and revalidation trigger; postponed does not mean passed.
- [ ] Record throughput, storage/clip, GPU/CPU cost, and caption/retrieval quality on representative private videos.

## Verification requirements

Sampling, model evidence, failure, privacy and invalidation tests plus local relevance evaluation; no personal media/evidence committed.

## Maintainer decision — postponed representative-video coverage (2026-11-01)

The maintainer has approved **postponing representative-media verification that the
current admission profile or available local footage cannot support**, rather than
expanding video formats, byte/duration limits or codecs solely to complete WI-0190.
This is a scoped acceptance-plan change, **not** a declaration that those cases pass,
not permission to bypass the proxy safety fences, and not a reason to postpone the
working SDR FrameEvidence pipeline. Workitem status remains `in_progress`.

Maintained-host, metadata-only observations (no filenames, content or personal paths
recorded): the inspection cohort contains 31 Rec.709 (`bt709`) video revisions,
25 HLG (`arib-std-b67`), one unsupported Rec.709 revision and one supported
revision with unspecified color transfer. There are 24 published PlayableProxy
receipts across two versions (13 compact 540p and 11 standard 720p), with a longest
published source of 49.4 seconds. These are job/receipt counts, **not** a verified
count of distinct source videos across profiles. All listed published sources
had `-90` display rotation.

Among nine inspected videos lasting at least 60 seconds: three use HLG, five
Rec.709 videos use `yuvj420p`, and the remaining 172.9-second Rec.709 HEVC
`yuv420p` source is 164.8 MiB but has **NULL display rotation**. The current
proxy query requires an explicit rotation in `(0, 90, -90, 180, -180)`, so
that source is not currently admitted; NULL must not silently be interpreted
as 0 degrees. A video marked `supported` by metadata inspection is not
automatically supported by the more restrictive proxy policy.

| Representative case | Decision for this WI-0190 verification pass | Reopen when |
| --- | --- | --- |
| Short, already-published local SDR, `-90` rotated phone footage | **Test now**: real frame captions, embeddings, interval/scene coverage as applicable, provenance, retrieval quality and resource use | No deferral |
| Medium/longer real SDR footage (including 172.9 s with NULL rotation) | **Postponed**: no qualifying published proxy yet; synthetic bounded long-duration sampling still tested | Rotation handling is reviewed and the video has a current approved, published proxy; or another admissible longer clip is found |
| HLG/HDR (`arib-std-b67`) | **Postponed** for positive inference and quality coverage; continue to verify explicit policy rejection | A reviewed HDR/tone-mapping/color pipeline creates eligible proxies without silently misrepresenting colors |
| Rec.709 `yuvj420p` | **Postponed** for positive inference and quality coverage; continue to verify explicit policy rejection | Full-range pixel-format admission, levels handling and proxy verification are implemented |
| Other extensions/containers, codecs, sources over 256 MiB or over 600 seconds | **Postponed where unsupported or absent**, not claimed to be observed in this cohort | The corresponding enumerator, admission policy and guarded processing budget are expanded and locally tested |
| Genuine landscape, multi-scene and low-light imagery | **Test if a suitable published clip can be identified; otherwise postpone each missing case separately**. Do not infer scene or lighting solely from metadata | Representative supported footage is identified or newly supported input yields a published proxy, with private human labels |

The current bounded FrameEvidence one-shot, rejection, exclusion, cancellation,
staleness, atomic-publication, privacy and synthetic duration/scene-plan tests
**remain applicable now**. In particular, postponed positive HDR/format processing
never excuses accepting an unsupported original into the video worker. Continue
the existing two-plus-clip private caption/retrieval assessment and measured
CPU/GPU/storage/cost campaign on available proxies. The real interruption
campaign remains restricted to a disposable isolated PostgreSQL database.

When video support is deliberately expanded in a later scoped repository workitem
(for containers/extensions, codec/pixel format/HDR, NULL rotation, source bytes or
clip duration), revisit these postponed rows against the new explicit policy,
regenerate a **new** approved proxy where required, and repeat the relevant
selected-one-shot, read-only frame audit, human coverage, live retrieval and
cost/quality assessments. The expansion is **not authorized by WI-0190**.
No OneDrive hydration or original modification is authorized. This decision
does not retroactively check any of the four full acceptance boxes; accepted
evidence and postponed exceptions must be reported separately.

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
approved scratch. The historic interval identity did not encode requested frame count,
which could collide with a previous succeeded two-frame job for the
same source/tool. The new interval policy explicitly encodes **1, 3
or 4 frames** (video-frame-sampling-interval-v2-n1/n3/n4),
while retaining the previous exact two-frame identity and allowing
prior v38 rows to regenerate using their stored requested_frames.
The existing manual scene policy already includes frame count
and cuts. Pure tests compare deterministic job keys and restore
the exact new interval policy without migrating old evidence.

The scene-policy identity, tool digest, revision
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

## Development checkpoint 16 — measured selected job and controlled interruption tooling (2026-11-01)

After merged PR #695, the selected FrameEvidence CLI supports
explicit 1–4-frame interval or manual scene policies with distinct
frame-count provenance, a fenced v38 full-batch publisher, and an
operator-only marker/lock-based crash scratch recovery tool. Before
a consolidated local quality/throughput session, the remaining
tooling gap was **safe repeatable measurement and a controlled
process-interruption rehearsal**.

This checkpoint adds two new explicitly invoked Windows commands:

- video frames acceptance-harness: accepts only the previously
  reviewed selected-one-shot arguments and a separate harness
  --execute acknowledgment. Defaults to a preview that does not
  launch the child, read the database, run models or disclose
  pixels. The actual child is the **same PhotoIdentity CLI
  executable**, with no arbitrary executable/PID input. Its
  output streams are discarded. It records measured process
  wallTimeMs, CLI-host-only cumulative CPU milliseconds and peak
  working set; it explicitly sets full cpuTimeMs, GPU time and
  Azure billing to **null** (not zero), since these cannot be
  reliably attributed to FFmpeg/Ollama/remote Azure from the CLI
  process. An optional interrupt mode requires an additional
  --acknowledge-owned-child-termination and a 5–600-second
  countdown, and terminates only the spawned child process tree
  under Windows.
- video frames interruption-audit: uses a **read-only repeatable-read
  PostgreSQL snapshot** and non-deleting owned-scratch inspection
  to report whether the specifically selected FrameEvidence job
  has any partial/published rows, active lease/authorization,
  uncleared operator video control, and remaining marked/unknown
  scratch. It intentionally returns incomplete while a job is
  not terminal, the scope is still selected, or the scratch
  must age before recovery. Nothing is migrated, deleted,
  enqueued, processed, disarmed or hydrated.

The existing video jobs stop/recover operators can restore a
**disposable isolated** rehearsal database after bounded
lease/grant expiry, but these commands affect every queued/
expired video job in that database and must NOT be used on the
maintained shared catalogue for this test. Owned crash scratch
remains subject to the previously implemented **6-hour minimum
age** and requires a later operator-approved recovery. A
remotely submitted controlled Azure inference call might
continue after the local process is killed; this is never
falsely reported as remote cancellation.

Synthetic subprocess tests verify exact CLI-spawn authorization,
redacted output, explicit kill gating and OS child-tree
termination, with pure incomplete/active-lease policy tests.
Disposable PostgreSQL integration proves read-only current
control state without mutation. A new
[consolidated Windows acceptance runbook](../../operations/video-frame-consolidated-acceptance-session.md)
combines selected-case measurements, private acceptance-report
and live retrieval evaluation, real interruption and the
delayed recovery audit. No personal media is used in CI.

**Scope honesty:** process-local CPU and wall measurements are
useful, but complete CPU/GPU and billed Azure per-clip cost must
still come from independently instrumented operator-owned
resources; missing values remain null until measured.
No local verification is requested to review this PR, and
the already accepted 12.3-second two-frame audit need not
be rerun. WI-0190 remains `in_progress` with **0/4 full
criteria formally accepted** until the representative media,
human quality, actual costs and real interruption/recovery
campaign is finished.


## Maintainer verification — two-video four-frame acceptance baseline (2026-11-02)

**Verified on the maintained Windows host, PostgreSQL v38 (photoidentity_main).**
Two distinct already-published local SDR PlayableProxy video assets were
selected, each with an independent interval-sampled four-frame FrameEvidence
job. The operator approved frame disclosure to a controlled loopback
Ollama/Azure bridge. This is operator-reported local acceptance evidence,
not synthetic CI coverage; no originals or OneDrive hydration were used.

| Evidence | Run 1 | Run 2 |
| --- | ---: | ---: |
| FrameEvidence job UUID | [private Run 1 FrameEvidence UUID] | [private Run 2 FrameEvidence UUID] |
| PlayableProxy job UUID | [private Run 1 PlayableProxy UUID] | [private Run 2 PlayableProxy UUID] |
| Read-only frame audit | current-complete, 4/4 | current-complete, 4/4 |
| CLIP embedding dimensions | 512 | 512 |
| Measured harness wall time | 168,776 ms | 97,545 ms |
| Caption + embedding payload | 8,554 bytes | 8,442 bytes |
| Accurate human-judged captions | 4/4 | 3/4 |

Both dry-run admissions and measurement-harness previews exited 0;
both executed child commands exited 0 without termination. Independent
read-only v38 audits exited 0, reported complete current evidence, and
returned no private caption or embedding data. Both owned frame scratch
roots contained only their original ownership marker after processing.
Video control returned to disabled/paused/unselected, with no active,
queued or failed video jobs; photo work was idle and automatic photo
captions stayed disabled.

The independent read-only **video frames acceptance-report** exited 0
and verified **two different video assets** in one current snapshot:
2 clips, 8 frames, 16,996 payload bytes, 266,321 ms aggregate measured
wall time, and 1.802 observed frames/minute. Video durations were
approximately 41.5 and 49.4 seconds. Both were operator-classified
short/rotated. These payload bytes exclude PostgreSQL disk overhead,
indexes, WAL and the published MP4 proxies.

For human caption review, the maintainer reconstructed eight private
JPEG frames locally from SHA-verified published MP4 proxies at the
persisted timestamps, compared each to the saved caption, and updated
the acceptance report: 8 captions judged, 7 accurate, **87.5% observed
accuracy** (4/4 and 3/4). **One incorrect caption** on a dark image
asserted a person was holding a dark object when only an arm was
visible. Categorize as **low-visibility visual hallucination:
nonexistent object / unsupported holding action**. This is not proof
of a general error rate, nor a verified representative *low-light*
video category. No private image, prompt, caption, name, source path
or annotation belongs in repository documentation.

**Resource limitations:** execution wall time is observed; harness
CLI-only CPU readings (24,125 ms and 29,015 ms) omit FFmpeg and
local/remote model execution. Peak host working set was unavailable.
Complete CPU/GPU time and actual Azure billing were **not measured**;
report zero-valued aggregates have measured-clip counts of zero and
must not be treated as zero consumption. No real scene boundaries
were annotated: scene coverage is **unknown**, not zero or complete.

**Completed maintainer check — local live CLIP text-to-video retrieval
(2026-11-02).** After human inspection of the already-published proxies,
the operator authored **two distinct private text queries** with one
independently relevant video per query (one for each of the two current,
different source video assets). The manifest stayed under protected
LocalAppData, never in Git. The read-only
`video frames evaluate-live` completed with **exit 0** against
`photoidentity_main` and the approved WI-0126 CLIP model/tokenizer.
The report verified `clips=2`, `queries=2`, `relevantJudgments=2`,
`frameVectors=8`, `embeddingDimensions=512`,
`frameTopK=2`, `rankingK=2` and `modelDigestMatched=true`.

| Retrieval ranking policy | Hit@2 | MRR@2 | Recall@2 | MAP@2 | nDCG@2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Maximum frame cosine | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| Top-2 frame mean | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| Deduplicated normalized pooled cosine | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |

Because each query had exactly one judged relevant video, the
MRR@2 of 1.00 establishes first-ranked relevant results for both
queries under all three strategies. Hit@2/Recall@2 alone would be
nearly trivial in a two-video corpus. The study does **not** rank
the three strategies against each other, measure generalization to
large collections, or expose per-query private rankings.
Model preflight wall time: 608 ms; local CLIP text encoding:
17,320 ms; PostgreSQL read snapshot: 276 ms; scoring: 27 ms.
The four measured phase times sum to 18,231 ms, not system CPU
or GPU time. The run reported `readOnly=true`,
`currentSnapshot=true`, `localTextInference=true`,
`loopbackPostgresReadPerformed=true`,
`externalNetworkRequestsMade=false`, no originals opened, no
OneDrive hydration or media decoding, no caption/vector/query
text returned and no job or schema mutation. **This retrieval
baseline is accepted; do not repeat it simply to close the item.**
The [live retrieval runbook](../../operations/video-frame-live-retrieval-evaluation.md)
records the verified aggregate result and the local procedure.

## Precise remaining WI-0190 completion gates (2026-11-02)

The four formal acceptance checkboxes above **remain unchecked**
until the combined evidence below is reviewed, rather than marking
each checked merely because some constituent positive tests passed.

| Criterion | Evidence already accepted | Outstanding for formal closeout |
| --- | --- | --- |
| 1. Supported media succeeds or safely fails | Two distinct current SDR proxies, four frames each, 512-dimensional CLIP, captions, audit, no originals/hydration, normal scratch cleanup | Verify and record **FrameEvidence-specific** unsupported/oversized/excluded/revised input refusal and no orphan scratch or new partial/current evidence. Existing isolated synthetic tests may support these negative cases; confirm they **actually ran** with a disposable PostgreSQL test-admin database, not skipped. Add a targeted local preflight only if a specific real-host safety boundary remains untested. |
| 2. Revision/model/policy regeneration and stale/partial isolation | Versioned provenance, deterministic frame-count policies, v38 atomic 4/4 publication, current read-only audits, CI integration tests | Verify the **negative** integrity path on a disposable database: stale newer revision, source exclusion, incomplete/cancelled publication and scoped authorization revocation do not appear as current. Complete the real owned-process interruption / after-expiry recovery study below; verify marker-only normal cleanup and conservative crash cleanup without touching the maintained catalogue. |
| 3. Bounded sampling / scene omission on admitted footage | Real four-frame interval jobs on two different short, rotated supported SDR videos; synthetic bounded planner and scene-identity tests | Inspect each available eligible clip **privately** for meaningful scene boundaries and execute read-only `video frames coverage` with actual cuts (`none` only if the operator genuinely judges a single scene). Record actual covered/missed counts and compare interval with hypothetical scene-balanced plan. If **no multi-scene/low-light/landscape/long** qualified published proxy exists, record that specific category as **postponed, not passed** under the 2026-11-01 approved deferral; keep synthetic long-duration and scene-limit/rejection tests. An actual scene-balanced `--apply` run is needed only when evaluating the real annotated-scene publication contract on suitable eligible media, with separate disclosure consent, not solely to repeat interval success. |
| 4. Throughput, storage, CPU/GPU/Azure cost, caption and retrieval quality | 266,321 ms two-video observed wall time, 1.802 frames/minute, 16,996 caption/vector payload bytes (not physical database size), 7/8 captions accurate with dark-frame object hallucination, 2-query top-1 retrieval for all three policies | **Measure** attributable full-system CPU (including FFmpeg), GPU processing on the inference host where available, and actual operator-controlled Azure billable cost for an explicitly selected fresh, distinct job; log physical proxy/database overhead where the criterion requires storage/clip rather than payload alone. Do not substitute CLI-only CPU time, GPU utilization %, advertised Azure list price or zero values for unmeasured totals. If a cost cannot be attributed, record it `null` and seek explicit maintainer scope decision rather than silently claiming criterion 4 complete. Expand quality evaluation on other **currently admissible** distinct videos/queries as needed to support a defensible representative assessment; do not fabricate a statistical threshold from two queries. |

**Mandatory real interruption/recovery gate (criteria 1–2):**
Use the [consolidated acceptance session](../../operations/video-frame-consolidated-acceptance-session.md)
in a **new disposable, already-rehearsed PostgreSQL database**,
with a disposable eligible proxy, fresh exclusive scratch root and
both existing explicit disclosure acknowledgments. Preview the
`acceptance-harness --mode interrupt` first, then independently
authorize its `--execute --acknowledge-owned-child-termination`
path. Require an actual `owned-child-terminated` result, not a
`child-exited-before-interruption`. Query the read-only
`video frames interruption-audit` after the operation, confirm
no published/partial FrameEvidence, no unexpected original/source
effects and no active grant/lease after normal fence expiry and
deliberate scoped recovery. Do **not** run global `video jobs
stop/recover --apply` on `photoidentity_main`; these can affect
other jobs. Recover only marker/lock-validated owned scratch
after the **six-hour hard minimum age** (default 24h) and repeat
the independent audit to exit 0, with no unrecognized workspace
children. Never fake the age or directly delete unknown files.

**Closeout mechanics and scope:** Keep all private queries, media,
captions, vectors, path names, PostgreSQL secrets, scene cuts and
frame JPEGs off GitHub. Record only sanitized metrics, test exits,
case categories, actual measurement coverage, failure reasons
and deferred-media revalidation triggers in this repository.
Run the targeted unit/integration tests with their disposable
PostgreSQL dependency **present**; require Windows CI and
`PhotoIdentity.Docs validate` plus `generate --check` to pass.
Request maintainer review that the remaining mandatory gates
were observed and the approved media deferrals are explicitly
accepted. Only **then** check the applicable four criteria,
move WI-0190 to `completed` through repository-native lifecycle
tools, archive its canonical status shard, and regenerate status
and roadmap views. WI-0191 production video search UI, WI-0207
OneDrive-only hydration and optional automatic remote dispatch
are separate work items and **not** WI-0190 closure blockers.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
