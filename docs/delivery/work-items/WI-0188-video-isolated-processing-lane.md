---
id: WI-0188
title: Introduce an opt-in, isolated video processing lane
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0150, WI-0151]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0188: Introduce an opt-in, isolated video processing lane

## Objective (scope revised by maintainer 2026-10-22)

Process videos after normal photo archive work, on demand through the same
Advance/Pause archive controls. The primary invariant is **never damage the
existing photo archive, catalogue, person identities, searches or collections**.
The user explicitly removed concurrent photo/video performance benchmarking and
the mandatory AppContainer feasibility requirement from this work item.

## In scope — revised

- Default-off **Process videos** switch in Settings, persisted across restarts.
  Shared **Advance archive / Pause archive** controls run photos first, then
  one eligible video at a time. Video can be disabled independently in Settings.
- **Include online-only videos** is an independently controlled opt-in, default
  off. Requests are bounded and do not hydrate by default. Only an
  app-initiated download may be released using OneDrive's online-only request;
  pre-existing local originals must remain local.
- Supported already-local video revisions use a bounded, fixed-tool-hash
  FFmpeg/FFprobe workflow. On unsupported codec, timeout or insufficient
  machine capacity, record a recoverable failure or diagnostic for future
  user review, including possible *separately designed* Azure processing.
- Recover an interrupted job or restart from the beginning; no frame-exact
  resumability required. Preserve immutable originals, fence stale
  revisions/exclusions, avoid exposing partial derivatives, and clean only
  owned scratch files.
- Basic progress / queued / succeeded / failed visibility with safe CPU,
  memory, wall-time, scratch byte and process-count caps. No configurable
  multi-video concurrency; one active video maximum.

## Out of scope — revised

- Simultaneous photo/video throughput or search/slideshow latency benchmarking;
  photos finish first and new photo work preempts videos.
- Mandatory AppContainer proof, comprehensive network egress restriction,
  kernel disk/IO quotas and GPU partitioning. Existing optional synthetic
  AppContainer lab can remain, but it is not a release blocker.
- Optional Azure compute fallback, advanced video proxy profile choices,
  captions, frame embeddings, browser video serving, and extra scheduling UI:
  these belong to later work items or separately scoped enhancements.
- Automatic deletion of the cloud original. A verified OneDrive *online-only*
  storage request is not deletion; a normal filesystem delete is forbidden.

## Acceptance criteria — revised

- [ ] **Default off and visible control.** Processing can be enabled/disabled
  in Settings, persists through restart, and shares normal Archive Advance/Pause
  controls. Only one video runs after all outstanding photo work completes.
- [ ] **Local-first with explicit hydration opt-in.** Offline placeholders are
  skipped by default. When the online-only setting is enabled, one bounded
  eligible source may be downloaded and subsequently requested online-only;
  already-local videos are not unpinned by Photo Identity.
- [ ] **Original/photo-data safety.** Video processing never opens originals
  for write or modifies photo catalogue/face/search/collection state. Source
  revision and exclusions are rechecked before durable derivative publication.
  Maintainer verifies post-run original integrity and ordinary photo features.
- [ ] **Failure recovery and cleanup.** Interrupted, failed, unsupported or
  oversized video jobs remain diagnosable; restart/retry is safe, partial
  outputs do not appear as complete, and owned temp data is bounded/cleaned.
- [ ] **Simple operational limits.** One video at a time, bounded Windows
  process resources and useful queue/failure counts; no mandatory AppContainer
  acceptance or synthetic-performance thresholds.

## Local verification follow-up — preflight diagnostics (2026-10-27)

- Maintainer started the published Windows app against schema v37 and enabled
  video processing. Photo synchronization completed across 17 included folders
  (30,777 enumerated files), but no video was analyzed.
- The observed operator status and single sanitized runtime warning were
  `video-safety-or-configuration-rejected`. The runtime rejected an
  `InvalidOperationException` before it could establish a successful video
  attempt; the supplied stdout does **not** identify which of the tool,
  private-cache or later safety checks rejected the run. In particular, it
  does not prove existing photo-only catalogue entries hide MOV/MP4 files.
- Follow-up changes make preflight stages visible as fixed path-free codes
  (`video-tool-configuration-rejected`,
  `video-ffmpeg-approval-rejected`,
  `video-ffprobe-approval-rejected`,
  `video-cache-validation-rejected`), retaining the existing runtime
  guards and fallback status for other failures. CI regression tests cover
  the classification and privacy requirements; no real-media test is claimed.
- Before accepting WI-0188, the operator must verify exact executable
  paths and SHA-256 approvals, private cache ownership/separation, and retry
  the real video discovery and processing sequence. CI coverage alone
  cannot verify the maintainer's OneDrive archive.

## Verification requirements — revised

CI covers focused deterministic settings/guard/queue tests and build/package
regressions. The maintainer explicitly accepts **manual local end-to-end
verification** of real video execution, cancellation, restart, exclusions,
OneDrive behavior, originals and ordinary photo features instead of elaborate
brittle automated E2E rigs. Follow
[the consolidated local verification runbook](../../operations/video-processing-shared-archive-verification.md).
Until real-file local verification is reported, **none of the newly revised
acceptance criteria is marked complete**.

Older numbered checkpoints below record historical design and experiments.
Their prior mandatory AppContainer, multi-job concurrency and mixed-load
benchmark language has been **superseded by this user-approved scope**.

## Implementation checkpoints

### Checkpoint 1 — admission/identity contracts (2026-10-19, implementation in progress)

- Added `VideoDerivativeJobIdentity` in Core: stable SHA-256 job key binds exact
  asset revision, derivative kind, policy version, output preset and operator-supplied
  local executable SHA-256. There are no private source names or paths in the key.
- Added `VideoProcessingBudget`, `VideoProcessingCandidate`,
  `VideoProcessingActivity` and the fail-closed, deterministic
  `VideoProcessingAdmissionPolicy`. Defaults are **disabled and paused**.
  Admission refuses stale/excluded/unsupported/online-only material, photo-busy
  periods, or resource estimates outside frame/source/temp/concurrency limits.
- Unit tests cover all denial reasons, safe default, budgets, validation and
  derivative idempotency/invalidation dimensions. No worker is connected and
  no queue or media processing runs because of this change.
- This is policy **preflight only**, not a security lease or resource-enforcing
  process supervisor. A caller must re-check revision, exclusion and source
  availability under authoritative repository locks and protect original bytes
  before execution. Maximum job lifetime still needs enforcement by the worker.

**Remaining work before WI-0188 review:** durable PostgreSQL job and control
records, atomic lease/restart/retry with cancellation and stale-output fencing;
strict local-process and hydration budgets; worker photo-priority/pause and
operator progress/diagnostics; default-off API wiring; atomic temp cleanup,
exclusion tests and maintained Windows mixed-load measurements. No existing
acceptance criterion is considered met solely by checkpoint 1.

### Checkpoint 2 — durable PostgreSQL control queue (2026-10-19)

- Schema v34 adds isolated video_processing_jobs with a video-metadata FK,
  bounded admission estimates, exact derivative/version/hash identity, unique
  idempotency key, sanitized states, attempts, cancellation and lease fencing.
  Existing photo processing_runs/jobs and video metadata/posters are unchanged.
- Repository exposes explicit enqueue (locked source, current revision, supported
  inspection, exclusion and source-byte checks), global-serialized bounded
  lease claims, progress heartbeats, cancellation, retry/restart recovery and
  fenced acknowledgements; all state transitions are database-only.
- Disposable PostgreSQL tests exercise no-job default, photo rejection,
  idempotency, active limits, queued/running cancellation, retry exhaustion,
  stale leases/revisions and exclusion hiding. No worker or browser endpoint
  consumes this queue yet; no original bytes are opened or derivatives written.
- New guarded 'catalogue rehearse-v34' CLI validates the current backup-restore
  report and only migrates a verified local isolated restored v33 database.
  See [v34 operator runbook](../../operations/video-processing-job-queue-v34.md)
  before any maintained-host schema upgrade.
- Lease ownership is **not** a file-publishing lock, locality attestation or
  process CPU/GPU/IO budget. Worker revalidation, atomic temp/derivative
  publication, active photo-priority sensing and actual managed hydration remain
  necessary before any runtime enablement.

**Checkpoint 2 acceptance (maintainer, 2026-10-19):** PASS: PostgreSQL
backup, isolated restore, v33 → v34 rehearsal, unchanged catalogue row counts,
empty video-processing queue, Release package, v34 health, and photo regression
checks. The optional Videos library check was N/A; no unexpected behavior
observed. This closes the checkpoint's schema rollout gate only, not WI-0188.

**Remaining before WI-0188 review:** worker execution controls, durable
resource accounting, safe cancellation of external processes, local derivative
cleanup, independent pause/visibility and mixed-load measurements. All WI-0188
acceptance criteria remain unchecked until the complete work item is verified.

### Checkpoint 3 — explicit queue observability and cancellation (implementation in progress)

- Read-only operator queue inspector with bounded 20-job view, aggregate status,
  expired-lease and reserved-temp metrics, no source paths or private metadata;
  excluded or deleted source copies remain invisible.
- Operator-only `video jobs status` and `video jobs cancel` commands; cancellation
  defaults to a dry run and requires `--apply` and exact job UUID. Both commands
  require localhost and an explicitly named, already-v34 catalogue, and neither
  starts workers or modifies originals.
- Disposable PostgreSQL visibility tests and CLI argument-gate tests. Does not
  enable automatic queueing or actual video processing.
- This is **not** a durable pause switch or a resource-enforcing runner. Worker
  admission, cancellation of an actual child process, kill-tree, safe cleanup,
  independent pause and photo-priority scheduling remain further checkpoints.
- Operator procedure: [v34 video jobs status/cancel](../../operations/video-jobs-operator-control.md).
  Since the maintained database is already verified v34, this checkpoint
  requires no schema rehearsal or restart solely for these CLI commands.


### Checkpoint 4 — subprocess supervision and exclusion race fences (2026-10-19)

- Added unregistered `VideoToolProcessSupervisor` with explicit SHA-256 executable
  approval, absolute local non-junction scratch root, per-call private work area,
  sanitized stdout/stderr discard, timeout, cancellation, process-tree
  termination, bounded scratch monitoring and post-exit cleanup.
- Hardened queue heartbeats to reject superseded, deleted, unsupported or
  source-copy-excluded revisions. Final job acknowledgements now lock the
  source row in a transaction before making the revision/exclusion decision,
  preventing an exclusion insertion from crossing the acknowledgement check.
- Windows Core tests cover digest mismatch, success/nonzero exit, scratch
  overage, cancellation and cleanup. PostgreSQL tests cover an excluded
  *running* job refusing both heartbeat and successful completion.
- The tool supervisor is deliberately **not connected** to API, CLI job commands,
  video-job scheduler, original-file access or FFmpeg. OS CPU/GPU/memory and
  disk quotas, audited FFmpeg arguments, durable pause, source authorization,
  atomic publication, and Windows mixed-load acceptance remain incomplete.
- [Process safety limitations](../../operations/video-worker-process-safety.md).

**Operator action:** no new maintained-database migration, backup, poster
generation, ingestion or video-processing activation is requested for this
checkpoint. Continue development after CI and PR review.

### Checkpoint 5 — PostgreSQL-backed photo-first video lease admission (2026-10-19)

- Added the read-only `PostgresVideoPhotoPriorityProbe`, which reports
  archive advancement requested/sync required, photo processing runs pending
  or running, and queued/running jobs in live photo runs. No changes to
  existing photo jobs or their lifecycle.
- All `PostgresVideoProcessingJobRepository.TryClaimNextAsync` calls now
  recheck the authoritative photo priority state inside the video claim's
  serialized PostgreSQL transaction. A caller supplying `photoWorkActive:
  false` cannot bypass photo work. A simpler overload defaults to the
  authoritative signal.
- Read-only `video jobs status` now includes the aggregate `photoPriority`
  flags, without private photo paths, identities or contents.
- Disposable PostgreSQL tests verify photo archive requests, pending runs,
  queued photo jobs, resuming video eligibility after the photo work is
  cleared, and no mutations to pre-existing photo processing tables.
- **Limits:** this is conservative *claim admission*, not a preemptive
  OS/process worker controller. A photo job starting immediately after claim
  may race the snapshot; search and slideshow activity outside the photo
  queue are not detected. An enabled worker must continuously sense priority
  and implement safe pause/cancel before WI-0188 activation.
- No migration, worker registration, video source open, file processing,
  OneDrive hydration or user configuration change occurs in this PR.
  [Operator note](../../operations/video-jobs-photo-priority.md).

### Checkpoint 6 — durable video pause/stop intent, schema v35 (2026-10-19)

- New append-only schema v35 adds a **single independent video control row**
  `video_processing_control`, with `enabled=false`, `paused=true` and a
  monotonically increasing generation from installation. It also adds the
  `control_generation` fence to video jobs; existing photo tables and all
  pre-existing catalogue row counts remain untouched by the migration.
- `PostgresVideoProcessingControlRepository` provides read-only state,
  explicit pause/resume intent, and a single-transaction stop that disables
  and pauses the lane, cancels queued video jobs, and marks running jobs for
  cancellation. Resume **never enables processing**; no worker/enable command
  is registered. All operator control writes are dry runs unless `--apply`.
- Video claim acquires a shared control-row lock inside its existing serialized
  transaction so stop/pause and lease admission cannot overtake each other.
  Lease heartbeat and completion also reject disabled/paused control state or
  a lease from an earlier pause/resume/stop generation.
  Operator status includes sanitized control state and generation.
- Disposable PostgreSQL tests assert default-off, stop/pause/resume/lease
  behavior, expired-cancel recovery and photo-table isolation. CLI tests
  cover dry-run/application and required options. Guarded `rehearse-v35`
  requires a verified local isolated v34 restore, checks unchanged original
  table counts, exactly one v35 migration and control disabled/paused.
- **NEW MAINTAINED DATABASE SAFETY GATE:** Do not start the newer v35 app
  against the currently accepted maintained v34 catalogue before a NEW
  authoritative backup and isolated restored v34 → v35 rehearsal succeed.
  The maintainer may defer this work until subsequent WI-0188 development
  checkpoints, but **not past the first v35 app startup**. Runbook:
  [v35 operator control and rehearsal](../../operations/video-processing-control-v35.md).
- No heavy background video worker, FFmpeg adapter, filesystem publication,
  automatic queueing, OneDrive hydration or hard process quotas are enabled.
  Continued acceptance requires source fencing, kill-on-priority worker
  preemption, hard resource controls and maintained Windows mixed-load tests.

### Checkpoint 7 — guarded subprocess preemption (2026-10-19)

- Added the explicit-call `VideoGuardedToolExecutor` to poll a caller-provided
  fail-closed execution safety signal, link its cancellation to the existing
  bounded `VideoToolProcessSupervisor`, and require a final safety read before
  considering a completed tool invocation eligible for review. It does not
  start a worker itself.
- Added a read-only `PostgresVideoLeaseSafetyProbe` using one PostgreSQL
  statement to check exact unexpired job token, current video revision,
  supported inspection and source exclusion, persisted operator control
  generation, cancellation, and photo advancement/processing priority.
- Synthetic Windows process tests cover photo-priority preemption, probe
  outage, denied start, successful exit and scratch cleanup. Disposable
  PostgreSQL tests cover mid-lease photo work, pause, stale generations,
  false lease token and stop.
- No schema migration beyond already-merged v35. **The maintained v34
  database still needs the separate v35 backup/restore/rehearsal safety
  gate before any v35 application startup.** No new local action is
  requested until subsequent worker checkpoints are complete.
- **Not a production worker**: live admission sampling has polling gaps;
  real OS CPU/GPU/memory/disk/IO quotas, source-open authorization, fenced
  file/database publication and mixed-load measurements remain outstanding.
  [Preemption boundaries](../../operations/video-execution-preemption.md).

### Checkpoint 8 — native Windows resource-envelope foundations (2026-10-19)

- Added the explicitly opt-in `WindowsVideoJobObject` controller with
  kernel-enforced job memory (128–4096 MiB), active-process (1–8), CPU-rate
  hard-cap (1–50%) and kill-on-close. A `WindowsVideoJobLimits` profile
  validates the requested envelope and fails closed when native setup or
  assignment is unsupported.
- The existing `VideoToolExecutionBudget` accepts optional `WindowsJobLimits`;
  when specified, the supervisor configures the OS job before process launch,
  assigns the child, and closes the job on every exit/cancellation path.
  Existing callers without an explicit profile remain unchanged, and no
  background worker, FFmpeg or video job execution is enabled.
- Windows Core tests cover profile bounds, native job-close termination and
  opt-in supervised cancellation/cleanup; non-Windows rejects a Windows-only
  job profile rather than silently relaxing it.
- **Safety limitation:** `Process.Start` currently runs before job assignment.
  This short uncontrolled interval **blocks production activation** until an
  atomically job-assigned or suspended launch is implemented and verified.
  Hard disk/IO, GPU, low-privilege/source-access isolation, publication and
  mixed-load performance tests are still required. See
  [Windows resource envelope](../../operations/video-worker-windows-resource-envelope.md).
- No schema changes; previously merged v35 still requires an isolated
  v34 → v35 rehearsal before the maintainer starts v35 code.

### Checkpoint 9 — suspended Windows process containment (2026-10-19)

- For explicitly capped native Windows profiles, replace the uncontained
  `Process.Start` → Job Object assignment sequence with `CreateProcessW`
  using `CREATE_SUSPENDED`, job assignment, and only then `ResumeThread`.
  Failure anywhere before resume terminates the never-run process. The
  subprocess has inherited standard handles attached to NUL; original paths,
  private stderr and launcher secrets are not logged or inherited.
- Existing legacy uncapped calls remain supported for prior tests, but
  production video workers must require an OS-capped suspended profile.
- Windows tests exercise successful native launch, cancellation, and
  bounded scratch teardown. The native child inherits only an explicitly
  allowlisted NUL device handle and a sanitized environment, never arbitrary
  photo-application handles.
  [Updated resource envelope](../../operations/video-worker-windows-resource-envelope.md).
- Added private `PostgresVideoLocalSourceLocator` and
  `VideoLeasedInputPreparer` to bind the actual source root/key/hash to
  the fenced PostgreSQL video job, check photo/operator safety, and stage
  only already-local MOV/MP4 into bounded, stream-hashed private scratch.
  Windows source opening requests `FILE_FLAG_OPEN_NO_RECALL`, avoiding
  intentional OneDrive hydration; maintained-host provider behavior still
  needs local verification. [Source staging](../../operations/video-worker-local-staging-and-cleanup.md).
- Added a deliberately unregistered `VideoUnpublishedToolAttempt` bridge
  with 15-second lease heartbeats, staged-only external input, required
  Windows Job Object limits and ongoing photo-priority preemption.
  **It does not acknowledge job success or publish derivatives**:
  versioned, revision-fenced atomic publication belongs to later media
  adapters; the video lane remains disabled by default.
- Added app-owned markers for tool/input scratch directories, a bounded
  conservative 24h+ `VideoScratchJanitor`, and dry-run-first
  `video scratch-cleanup` operator CLI. Tests cover unowned/recent folder
  preservation, verified source hash, lease revocation, and scratch cleanup.
  No photo archive or original media delete command is introduced.
- Native Job Object limits still do not enforce GPU, filesystem sandboxing,
  full disk/IO quotas, or safe source-content authorization. Those remain
  separate activation gates. No schema change; the already merged v35
  migration still requires maintained-machine rehearsal before startup.

### Checkpoint 10 — bounded unregistered worker-dispatch contract (2026-10-20)

- Added `VideoWorkerAttemptCoordinator`: one deterministic job per explicit
  invocation, requiring a caller budget with concurrency exactly one and
  authoritative v35 enablement/paused-state admission from PostgreSQL.
  Claims remain photo-priority-aware; the coordinator rechecks the exact
  lease before processing and bounds adapter lifetime.
- Introduced `IVideoPublishedDerivativeAdapter` as an **unimplemented**
  downstream artifact publisher boundary, intentionally not connected to
  any hosted service, API or operator command. There is no automatic
  video processing or new path that can turn it on.
- Adapter failures and unpublished results are handled through existing
  lease-fenced retry/terminal failure transitions, with sanitized outcome
  codes. The generic `TryFinishAttemptAsync(succeeded: true)` queue method
  now explicitly **refuses to mark uncommitted video jobs successful**.
  An in-memory `Published=true` report is not enough: the coordinator
  also reads durable success state. **Durable job status alone is
  not proof of a valid derivative file**; the concrete adapter must
  atomically commit and fence that artifact before activation.
- New dry-run-first `video jobs recover` operator command allows safe,
  bounded-scope recovery of expired video job leases when the lane is
  stopped and no hosted worker is present. It cannot touch photo jobs,
  source bytes or otherwise change video enablement.
- Disposable PostgreSQL tests cover default-off persisted control,
  one-job claim, fake published results, retryable failures with no
  private exception contents, pause veto and no photo job mutations.
- No schema migration; source and process containment from PR #617 remain
  unchanged. General [worker dispatch contract](../../operations/video-worker-dispatch-contract.md)
  describes the still-required production publisher and local acceptance.
  Schema v35 remains locally unverified against the maintained v34 database.

### Checkpoint 11 — video scratch-volume admission preflight (2026-10-20)

- Added `VideoScratchCapacityGuard` to fail closed unless the dedicated
  scratch directory is on an available, fixed local volume without linked
  ancestors and has the estimated write allocation **plus 512 MiB** free.
  Arithmetic remains bounded and overflow safe; tests cover exact-boundary,
  missing-directory, invalid estimate and combined input/output cases.
- `OneDriveVideoLocalStager` checks the expected source-copy bytes before
  opening the original. The unregistered `VideoUnpublishedToolAttempt`
  additionally checks the **combined** lease reservation for input plus
  tool scratch; the OS-capped supervisor rechecks the tool output allowance
  immediately before tool launch. Uncapped legacy synthetic tool calls are
  not implicitly opted in.
- This is a protective **preflight**, NOT a volume reservation, hard disk quota,
  write-I/O limit, filesystem sandbox or guarantee about changes after the
  check. The external encoder remains unregistered. Do not enable video
  processing until stronger containment/publication and maintained Windows
  throughput/OneDrive checks are accepted.
- No schema migration, API, source modification, OneDrive hydration or
  automatic video job activity. The maintained v34 → v35 backup/isolated
  rehearsal gate is still outstanding before the first local v35 startup.

### Integrated checkpoint 12 — v36 private derivative receipts and guarded publication (2026-10-20)

- Added v36 append-only `video_published_derivatives` registry.
  Private artifact receipts use exact job/revision, SHA-256, byte count and an
  opaque job/lease-derived path. Job success and receipt are written in **one
  PostgreSQL transaction** after current lease, video control generation,
  cancellation, latest supported revision and exclusion checks.
- The existing generic queue `TryFinishAttemptAsync(succeeded: true)` still
  fails closed. The single-job dispatch coordinator now requires both durable
  successful status and an independently read authorized publication receipt.
- Added an optional guarded successful-tool-exit callback, with live
  lease/priority observation, **before** supervised workspace cleanup.
  Publication strictly requires a **single-process native Windows Job Object**;
  uncapped/multi-process tool profiles fail closed before launch.
  An unregistered `VideoPublishedToolAdapter` integrates the previously
  separate staged input, Windows-capped process, optional callback and
  `VideoDerivativeFilePublisher` for a bounded same-volume private rename.
  No process, worker or video enablement is registered.
- Synthetic filesystem/disposable PostgreSQL and Windows callback tests
  cover output key binding, missing ownership, durable success, invalidated
  control, source exclusion, callback cleanup and unchanged photo operations.
  See [v36 publication and one consolidated v34→v36 isolated rehearsal](../../operations/video-publication-and-v36-rehearsal.md).
- **Remaining before acceptance**: WI-0189's audited FFmpeg/MP4 profile and
  independent media validation, database-aware orphan recovery and quota/
  privacy-aware eviction, real filesystem/IO/network limits, a production
  opt-in worker/selection interface, current Windows OneDrive no-recall and
  photo-only vs. mixed-load acceptance. A DB receipt is not proof of playable
  media, and the filesystem + DB are NOT one atomic transaction.
- The maintained v34 database MUST NOT start any v36 build until a **fresh**
  authoritative backup and verified isolated **v34→v36** rehearsal passes.
  This single exercise covers both previously pending v35 and new v36.

### Integrated checkpoint 13 — conservative v36 orphan reconciliation (2026-10-20)

- Added `VideoDerivativeCacheGuard` shared by the private publisher and the
  recovery tool. Both independently verify the exact owned root marker,
  absolute local unlinked directory and no overlap with registered local
  source archives. The guard neither creates roots nor reads originals.
- Added `VideoDerivativeRecoveryJanitor`: a bounded, nonrecursive scan of
  canonical, opaque job/lease MP4 files. The operator must hold an exclusive
  v35 video control-row lock, with the lane **disabled and paused** and no
  running video jobs. Only aged (at least 48 hours), **unregistered** outputs
  for known terminal jobs are eligible; default behavior is dry run.
- Existing **registered receipts are always protected**, even if excluded or
  superseded. The aggregate `invalidatedRegistered` count identifies work
  remaining for WI-0189's explicit invalidation/privacy purge and recovery
  lifecycle; this command will not create false `succeeded` records with
  missing files after reversible exclusion restoration.
- New operator CLI `video derivatives recover` requires an explicit local
  already-v36 database, exact cache root and optional bounded age/file count.
  It refuses implicit schema migration, exposes only counts, and never
  touches photo jobs, originals or unrelated folders. Synthetic PostgreSQL,
  filesystem and parser tests cover stopped/running control gates, valid and
  excluded receipts, recent output, unowned paths, archive overlap and
  dry-run/apply behavior.
- **No schema change after v36**, no registered worker or new enablement.
  [Runbook](../../operations/video-derivative-recovery-v36.md).
  The unfulfilled **fresh v34→v36 backup/isolated rehearsal** remains a
  mandatory gate before any maintained v36 application/CLI startup.
- Production blockers remain: audited playable proxy media contract and
  independently validated output, registered-artifact invalidation and
  privacy-aware eviction, enforceable disk/network/IO policy, production
  opt-in worker and maintained Windows OneDrive/photo-vs-mixed-load acceptance.

### Integrated checkpoint 14 — unregistered SDR proxy profile + independent output validation (2026-10-20)

- Added an intentionally experimental, **unregistered** 720p H.264/AAC
  MP4 candidate profile at the WI-0188/WI-0189 boundary. It uses fixed
  argument arrays, explicit file-only protocols, software libx264,
  bounded output/frame rate/time/size, preserved aspect and FFmpeg camera
  autorotation, stripped private metadata and Rec.709 output tagging.
- Strict revision-bound eligibility permits only already-inspected
  supported H.264/HEVC SDR **bt709** videos with bounded dimensions,
  duration and known rotation. HDR HLG/PQ, unknown transfer and
  unsupported codecs are deferred, never silently tone-mapped.
- A new separately hash-pinned FFprobe validator runs under its own
  native single-process Windows Job Object in a temporary workspace,
  with file-only protocols and a 64KiB JSON limit. Before v36 durable
  publication, checks include faststart MP4 box ordering, actual size,
  H.264 yuv420p SDR color metadata, dimensions/framerate, normalized
  rotation and optional AAC mono/stereo audio with no extra streams.
  A failed check cannot record successful derivative publication.
- Added synthetic negative/positive tests for codecs, HDR/unknown color,
  profiles/metadata, rotation, stream count, output dimensions/rates,
  malformed boxes and canonical argument arrays.
- **No new migration, worker/CLI activation, bundled FFmpeg binary,
  OneDrive hydration, source modification, photo feature change or
  production-profile approval.** Real-world codec compatibility,
  quality, audio sync and browser seek behavior remain WI-0189
  acceptance work. Hard quotas, network/filesystem confinement,
  reviewable enablement and mixed-load measurements remain WI-0188.
- See [SDR candidate and deferred Windows acceptance](../../operations/video-proxy-pilot-validation-v36.md).
  The maintained v34 catalogue still requires the **fresh backup +
  isolated v34→v36 rehearsal** before any v36 startup.

### Integrated checkpoint 15 — source-free synthetic real-tool roundtrip (2026-10-20)

- Added an **operator-only, dry-run-first, database-free** CLI
  `video synthetic-proxy-pilot`. It cannot take a user-media source path;
  it generates its own three-second landscape+audio and portrait+silent
  fixture under a dedicated, otherwise-empty, marker-protected local
  temporary root, then passes the fixtures through the **same fixed
  `VideoProxyCandidateProfile.BuildArguments`** used by the still-unregistered
  video adapter.
- The independently SHA-256-approved FFmpeg and FFprobe processes each run
  within single-process native Windows Job Objects with bounded CPU, RAM,
  wall time, workspace bytes and no inherited application credentials.
  Candidate MP4s pass the independent faststart, codec, SDR, duration,
  orientation and optional AAC probe verification before success is reported.
  All generated media/probe workspaces are removed after execution.
- Command reports only aggregate synthetic video orientation, dimensions,
  encoded bytes, audio presence, duration and elapsed encoding time.
  It does **not** open PostgreSQL, start/enable a video worker, enqueue jobs,
  access archive originals, hydrate OneDrive, expose video APIs or affect
  photo processing. No v36 schema migration is involved.
- Deterministic tests cover parser safety, strict scratch ownership/empty
  root, no archive input, dry-run, fixed synthetic argument arrays and
  forbidden extra source options. An **environment-gated Windows
  integration test** can run a real installed FFmpeg/FFprobe roundtrip
  without requiring tool installation or external media in ordinary CI.
- See [synthetic pilot and optional Windows procedure](../../operations/video-synthetic-proxy-pilot.md).
  This is a development verification instrument, not WI-0189's
  representative camera-media or browser acceptance. The maintained
  v34→v36 authoritative backup+isolated rehearsal remains mandatory
  before starting any v36 app/CLI *against the maintained DB*.
- **Remaining**: reviewed real-media encoding/preset and retention/eviction
  (WI-0189), hard video IO/disk/network isolation or approved measured
  envelope, explicit production video worker/selection controls, and
  consolidated Windows OneDrive no-recall/photo-priority/throughput acceptance.

### Integrated checkpoint 16 — registered derivative lifecycle (2026-10-20)

- Added **database-first, operator-only** registered v36 derivative maintenance.
  It closes a critical gap that the earlier orphan janitor intentionally
  left open: exclusion, deleted/superseded revision, missing cache bytes,
  corrupt cached content and optional over-quota current derivatives.
- A single bounded transaction requires the video lane **disabled+paused**
  and no running leases, locks affected source/job/receipt rows, selects
  at most 32 registered files and atomically deletes their receipts while
  moving their durable succeeded job to failed/`revision-invalid`.
  Only **after** committing that invisibility fence does it unlink exact,
  owned private MP4 paths. A failed unlink remains an invisible orphan
  recoverable by the existing stopped-lane janitor. No external source
  archive files are touched.
- The new dry-run-first `video derivatives maintain` command optionally
  reports an aggregate registered-byte budget (64..16384 MiB),
  minimum healthy quota age, bounded batch size, and SHA-256 byte
  verification. This is logical cache maintenance, not an OS hard
  filesystem/IO quota.
- A separate explicit UUID-only, stopped-lane
  `video derivatives requeue` command allows a revoked job to re-enter
  `queued` **only after its exact source and revision are again
  supported, latest, unexcluded and receipt-free**. It does not claim
  or process the job; generic production video activation is still absent.
- Deterministic CLI tests and isolated PostgreSQL+synthetic private-file
  tests cover exclusion and restoration, missing and same-size-corrupt
  files, oldest-first quota eviction, no-op dry runs, disabled/running
  lane fences, requeue, and original/private-entry preservation.
- No schema migration after v36, hosted video process, API exposure,
  OneDrive hydration or alteration of photo workflows. See
  [registered derivative lifecycle and deferred operator procedure](../../operations/video-registered-derivative-lifecycle-v36.md).
  The maintained schema v34→v36 authoritative backup and isolated
  rehearsal gate remains mandatory before starting any v36 app/CLI
  against the maintained database.
- **Still outstanding**: approved real-media quality/browser seeking
  (WI-0189), hard or explicitly accepted measured filesystem/IO/network
  isolation, future single-job production selector/enablement,
  source exclusion full purge integration, and a consolidated
  Windows photo-only vs mixed-load/OneDrive no-recall benchmark.

### Integrated checkpoint 17 — explicit time-limited selected-video one-shot (2026-10-20)

- Added an **append-only schema v37** selected-video authorization field
  pair on the v35 video operator control row. Both fields are NULL by
  default on restored databases; the video lane remains disabled/paused.
  A single deliberate grant binds exactly one queued job UUID and an
  absolute 13-minute expiration to a new control generation. Source,
  revision/exclusion, photo-priority and zero-running-job checks must
  pass before this narrowly scoped enablement can be issued.
- Extended **queue claim SQL, source locator, heartbeat, live safety
  probe, retry acknowledgement and final v36 publication** to honor the
  selected UUID and deadline. A crash cannot leave an indefinitely
  active authorization; any authorization expiry vetoes claims,
  source staging, tool execution renewal and publication.
- Added two explicit operator-only commands. `video one-shot enqueue`
  accepts only an existing revision UUID and approved FFmpeg hash,
  probes **filesystem availability metadata only** (no content read or
  OneDrive hydration), and optionally persists an idempotent fixed-preset
  PlayableProxy job. `video one-shot run` requires that exact queued
  job, a locally marked cache, two SHA-256 pinned binaries, a **dry-run
  first**, and separate `--apply` plus v37 rehearsal acknowledgement.
  It authorizes one bounded Windows Job Object attempt, runs the
  previously unregistered SDP/MP4 validation/publisher integration and
  disarms the matching generation after completion. No unrelated queued
  job is cancelled by the scoped disarm.
- New disposable PostgreSQL tests cover wrong-job admission, operator
  photo/lease gates, selected-only claims, expiry-preempted heartbeats
  and private-source access, exact-match disarm and preservation of
  unrelated queued jobs. Parser tests refuse broad source-path
  injection, missing rehearsal acknowledgement, wrong hashes and
  duplicate/unrecognized options.
- The isolated restore rehearsal now applies v34→**v37** in one
  sequence and checks the null/default-off selected-grant fields,
  migration records and preserved table counts.
- No hosted worker, unattended requeue, source hydration, photo pipeline
  change or public API registration. No production enablement is
  approved. See [v37 selected trial operator runbook](../../operations/video-scoped-one-shot-v37.md).
  The maintained v34 PostgreSQL database must not be opened by the
  newer v37 app/CLI until a **new authoritative backup and isolated
  v34→v37 rehearsal** have passed.
- Remaining WI-0188/WI-0189 gates: comprehensive hard disk/IO/network
  containment or reviewed measured limits, representative real MOV/MP4
  playback/audio/color and browser seek acceptance, proxy policy,
  automatic privacy cleanup, and consolidated photo-only versus
  mixed-load/OneDrive no-hydration measurements.

### Integrated checkpoint 18 — first maintained v37 regression observations and diagnosable synthetic execution (2026-10-20)

- Maintainer-reported **PASS** across all nine photo regression groups:
  Archive browsing, People/faces, visual/caption/combined search,
  Smart/Creative Collections, photo metadata, slideshows and photo
  archive processing. Local application health is `ok`, PostgreSQL
  `ready` and `schemaVersion=37`.
- Read-only `video jobs status` reports default-off `enabled=false`,
  `paused=true`, `selectedJobId=null`, no authorizations, no
  queued/running/succeeded/failed/cancelled video jobs, no reservations and
  photo priority inactive. Disabled video endpoint returned HTTP **404**.
- The database-independent synthetic FFmpeg pilot **dry run passed**, but
  **actual `--apply` failed with `status=pilot-failed` and zero results**.
  This is explicitly **not** video encoding acceptance. The prior CLI
  suppressed all failure detail; neither the stage nor a safe reason
  code could be recovered from its JSON. The root cause on the
  maintainer's Windows toolchain is **not yet identified**.
- Updated the synthetic pilot to report a strict allowlist of failure
  stage, fixture orientation, reason class and, when known, numeric tool
  exit/Windows error. Independent FFprobe launch/exit/preemption are
  distinguished without disclosing paths, media, secrets or tool stderr.
  Windows no-tool and error-class tests are included. No database
  migration, original-file access or worker activation is introduced.
- **Reported evidence limits**: no backup/isolated v34→v37 rehearsal
  output was included in this report, so do not claim this separately
  proven here. Video-library positive browsing and private cache
  janitor previews remain optional/unreported. Continue to require
  the backup/isolated rehearsal and explicit approval gates for any
  future actual video one-shot test.
- **Next maintainer action after CI/merge**: if the marked lab root
  is empty except for its marker, rerun the *synthetic-only* test once
  and share the sanitized output. Do not run real-media one-shot,
  disable containment or alter private originals while diagnosis
  is outstanding.

### Integrated checkpoint 19 — Windows process safety rejection triage (2026-10-21)

- Maintainer retried merged synthetic pilot; the already-accepted v37
  application/photo checks remain unchanged. Pilot continues to **FAIL**
  before first landscape fixture is generated, with
  `failureStage=fixture-generation`,
  `failureClass=execution-safety-rejected`, no process/Win32 exit
  code and zero results. This is **not** codec validation, FFmpeg
  runtime acceptance or evidence of source/archive access.
- Added an optional, source-controlled **`failureCheckpoint`** to the
  synthetic-only sanitized report. The supervisor tags existing
  `InvalidOperationException` objects without changing their
  catch behavior or bypassing guards. It distinguishes tool SHA
  mismatch, linked executable/path, native Job Object setup,
  suspended process launch, process monitoring, output verification,
  scratch resource limit and shutdown failure. The checkpoint
  can only be a fixed allowlisted string; arbitrary exception
  messages, paths, subprocess logs and media metadata are never
  reported, and nested failures retain the original innermost code.
- New tests check allowlist rejection, existing exception type
  stability, mismatch classification using approved-hash shape,
  no subprocess invocation, and private scratch cleanup.
- **Next proof**: GitHub Windows CI green, followed by exactly one
  synthetic-only rerun by the maintainer with the same approved
  binaries and scratch marker. No new PostgreSQL migration, video
  worker enabling or real-video processing is part of this fix.

### Maintainer local checkpoint 20 — synthetic proxy validation accepted (2026-10-21)

**Result: PASS for the synthetic-only Windows encoder/validator roundtrip.**
The approved native FFmpeg/FFprobe binaries had originally been invoked
through a Windows linked executable path and were correctly refused at
`failureCheckpoint=linked-executable`. The maintainer resolved the
installed tool paths to the real files and verified them against the
previously accepted ADR-0012 SHA-256 fingerprints, without disabling
the reparse-point guard or weakening resource limits.

A fresh read-only `video synthetic-proxy-pilot` preview returned
`dryRun=true`, `status=not-run`, and `schemaUsed=false`.
The explicit `--apply` rerun returned `status=validated` with two
independently FFprobe-validated cases and null failure fields:

| Source fixture | Validated output | Audio | Duration | Size | Encode time |
| --- | --- | --- | --- | --- | --- |
| Landscape 1280×720 | 720×406 | AAC present | 3 s | 218430 bytes | 793 ms |
| Portrait 720×1280 | 406×720 | None | 3 s | 184101 bytes | 470 ms |

The report explicitly states `schemaUsed=false`,
`originalsOpened=false`, `videoWorkersActivated=false` and
`derivativesRetained=false`. This closes **only** the optional
source-less codec/containment smoke-test checkpoint, not WI-0188 or
WI-0189. The project already recorded independent PASS for nine
photo regression areas, v37 health, the disabled/unselected job lane,
and the disabled Videos API. The backup/isolated v34→v37 rehearsal
output was **not included in the same reported results**, so retain
that separate prerequisite as unverified in this evidence record.

**Remaining acceptance**: one selected real and already-local SDR
video trial, authoritative source checksum/no OneDrive hydration
evidence, bounded resource and cancellation/restart/exclusion behavior,
photo-only versus competing-video throughput/search/slideshow latency,
and reviewed hard filesystem/IO/network or alternate containment.
No persistent unattended worker, media API or production activation
is authorized by the successful synthetic test.

### Maintainer checkpoint 21 — full maintained v37 verification confirmed (2026-10-21)

**Updated evidence from the maintainer:** the earlier checkpoint-18/20 notes
incorrectly left the backup/rehearsal and positive Videos library checks as
unreported. The maintainer has now explicitly confirmed **PASS** for every
item below during the same local verification session:

| Verification category | Maintainer result |
| --- | --- |
| Fresh authoritative PostgreSQL backup | PASS |
| Isolated PostgreSQL restore | PASS |
| Guarded v34→v37 rehearsal | PASS |
| Existing catalogue counts preserved | PASS |
| Repeated schema initialization | PASS |
| Maintained app health, schema v37 / PostgreSQL ready | PASS |
| Video control disabled, paused, no selected job | PASS |
| Existing photo Archive browsing | PASS |
| People library and manual face review | PASS |
| Visual, Caption and Combined search | PASS |
| Smart and Creative Collections | PASS |
| Photo metadata editing | PASS |
| Existing slideshow playback | PASS |
| Videos library and previously created still posters | PASS |
| No unintended OneDrive hydration or original-byte changes | PASS |
| Synthetic proxy preview | PASS |
| Independent FFmpeg/FFprobe synthetic roundtrip | PASS |

The accepted synthetic outputs were **720×406 / AAC / 3 seconds**
and **406×720 / silent / 3 seconds**, both H.264 SDR Rec.709 validated,
after selecting physical tool paths matching approved ADR-0012 hashes.
The CLI reported no database access, original-file reads, background video
workers or retained synthetic derivatives. These are maintainer-reported
acceptance results; private database dumps, source names and media evidence
are not committed to this repository.

**Accepted checkpoints:** source-less codec smoke test, maintained v37
migration/restore/idempotency, default-off operator state and integrated
photo/Videos-library compatibility. This supersedes only the earlier
**unreported** status for those checks; the preceding checkpoint notes
are preserved as an audit trail.

**WI-0188 remains in progress:** no real private-video proxy has yet
been produced via the selected one-shot lane; explicit privacy isolation,
bounded source copy/IO and OneDrive placeholder behavior, cancellation/
exclusion/restart protection under real load, and photo-only versus mixed
CPU/latency evidence remain to be verified before production enablement.

### Development checkpoint 22 — bounded read-only v37 candidate discovery

- New optional `video one-shot candidates` command accepts only a named
  localhost PostgreSQL connection variable, explicit current-schema v37
  database and optional `--limit` (1..20; default 12).
- It performs **metadata-only** SQL against currently latest, unexcluded,
  nondeleted, locally registered video revisions with fixed experimental
  Rec.709 codec/pixel format, duration, rotation and input-byte bounds.
  Results contain only opaque revision UUIDs, source byte counts, numeric
  duration, raw coded orientation and presence of audio. Ordering is
  deterministic, shortest duration first, and all results are bounded.
- No archive path or source key is fetched, no file is opened, and no
  OneDrive availability or hydration API is called. No DB migration,
  job creation, queue activation or tool execution occurs. The output
  explicitly says `localAvailabilityChecked=false`: this is **not**
  proof that a candidate is already available on disk.
- The existing `video one-shot enqueue` **without `--apply`** remains
  the mandatory exact-revision availability/inspection and size gate.
  No real-media enqueue or one-shot apply is requested by this PR.
  Test coverage pins parse/refusal rules, empty/no-DB behavior, and
  privacy-safe output.
- Real-media trial approval remains a separate decision after reviewing
  local free-space, IO containment and photo-priority risk.

### Maintainer checkpoint 23 — exact local video eligibility confirmed (2026-10-21)

The maintainer's first `video one-shot candidates` metadata-only query
returned **one** possible SDR video: 12.3 seconds, coded landscape,
21,687,565 bytes, and audio present. The exact-revision
`video one-shot enqueue` command was then run **without
`--apply`**, returning `dryRun=true`, `eligible=true`,
`jobId=null`, `mediaOpened=false` and `videoExecuted=false`.

**Accepted:** one latest, nonexcluded, technically supported source
revision passes size and *currently local* availability metadata checks.
The source bytes were **not** opened; there was no video job, FFmpeg
execution, hydration or durable derivative. The private UUID and
source/archive paths are intentionally excluded from tracking.

### Development checkpoint 24 — independent no-job one-shot environment review

- Adds `video one-shot environment`, a Windows-only command that
  accepts explicit v37 localhost DB, marked private cache root and
  already-approved physical FFmpeg/FFprobe paths and digests.
  It is read-only and **cannot** accept `--apply`, `--job-id`,
  `--revision-id` or a source path.
- Checks disabled/paused/unselected/no-expiration video control,
  zero running video leases, idle authoritative photo jobs,
  private-cache ownership marker, no archive/cache overlap,
  unlinked local directory, OS fixed-volume free space for
  a full 512 MiB reservation plus 512 MiB headroom, and both
  unlinked executable digests against the selected approvals.
- Reuses the **exact existing** v37 cache guard and the one-shot
  executable verifier; this adds no novel media I/O and no
  modified authorizations. JSON is source-free, explicitly
  `dryRun=true`, `realVideoExecutionAuthorized=false`,
  `hardDiskIoQuotaVerified=false`, and
  `networkSandboxVerified=false`.
- Test coverage refuses real-media command flags, checks
  all active control/photo-priority cases and asserts neither
  media access nor hard-IO/network isolation is claimed.
  The operator runbook documents creating only a new marked
  private root outside source/OneDrive directories.
- **Outstanding:** independent original-integrity assurance and
  photo-only/mixed-load measures, plus reviewed hard disk/IO/network
  isolation (or a specifically approved measured substitute) before
  actual real-video trial or milestone completion.

### Maintainer checkpoint 25 — one-shot Windows environment preflight PASS (2026-10-21)

The maintainer merged PR #641 and ran the environment-only command on the
maintained **schema-v37** system using the reviewed private cache and
approved unlinked physical FFmpeg/FFprobe executables. Returned evidence:

- `dryRun=true`, `passed=true`, `reasonCode=preflight-only`,
  `schemaVersion=37`.
- `originalsOpened=false`, `oneDriveHydrationRequested=false`,
  `videoWorkersActivated=false`, `jobsEnqueued=false`,
  `derivativesPublished=false`.
- `hardDiskIoQuotaVerified=false`,
  `networkSandboxVerified=false`,
  `realVideoExecutionAuthorized=false`.

**Accepted:** exact current selected-trial environment readiness,
private-cache ownership and non-overlap, approved tool hashes and
available capacity, and current disabled/paused/unselected/idle
admission checks. The command does not inspect original video bytes
or authorize video processing. Do not repeat the accepted synthetic
or v34→v37 migration checks merely for this step.

### Development checkpoint 26 — explicit no-recall source integrity read

- Adds `video one-shot source-proof` accepting **only** a previously
  ingested video revision UUID, the verified current v37 localhost
  PostgreSQL reference and optional `--read-local-bytes`.
  Default mode performs **zero database or source reads**, reporting
  `not-run`. There is no `--apply`, arbitrary media path, enqueue,
  FFmpeg execution, publication or worker activation.
- Only after an operator deliberately selects `--read-local-bytes`
  does it fetch the exact latest, supported/nonexcluded source
  identity, size and catalogued SHA-256 from a **read-only**
  PostgreSQL transaction. A fresh stopped/paused/unselected lane,
  zero running video leases and idle authoritative photo activity
  are also required before any source-byte IO. It checks already-local OneDrive status,
  bounded 256 MiB size, unlinked source and ancestors, and streams
  at most the approved file length using the same Windows
  `FILE_FLAG_OPEN_NO_RECALL` handle already used for leased staging.
- It compares the SHA-256 against the original catalogued revision,
  checks source size/mtime/local status again after reading, and
  returns only allowlisted outcome codes and booleans. Unknown
  interrupted-read state is reported as `null`, never falsely
  `false`. No filenames, paths, original hashes or media bytes
  are printed or persisted.
- Shared no-recall reader replaces the original private P/Invoke in
  the existing video stager with the **same handle flags and access
  rights**. This changes no staging policy or source write behavior.
  Windows synthetic file tests cover matching/mismatching content,
  size rejection, privacy-safe outputs and opt-in parsing.
- A successful source-proof will establish **pre-trial** original
  integrity only. It **does not** prove post-video source integrity,
  hard disk/IO/network containment, photo-interactive performance or
  authorization to execute the real-video worker. The maintainer
  must explicitly authorize original byte reads by choosing the
  flag after review.

### Maintainer checkpoint 27 — explicit pre-trial source checksum accepted (2026-10-21)

The maintainer executed both modes of `video one-shot source-proof` for
the previously selected eligible local SDR revision. The default call
returned `dryRun=true`, `explicitLocalReadRequested=false`,
`status=not-run`, `originalBytesOpened=false`. The expressly
authorized `--read-local-bytes` call returned:
`status=verified`, `revisionDigestMatched=true`,
`originalBytesOpened=true`, `sourceMetadataStable=true`,
`originalWriteAccessRequested=false`,
`oneDriveHydrationRequested=false`, `jobsEnqueued=false`,
`videoWorkersActivated=false`, `derivativesPublished=false`,
`originalPostTrialIntegrityVerified=false`, and
`realVideoExecutionAuthorized=false`.

**Accepted:** the exact original's currently catalogued SHA-256 before
any video processing. This is **not** post-trial byte integrity and
does not prove a general cloud/no-network OS sandbox. No source
filename, source path, SHA-256 bytes, private media content or
credential is recorded in the public work item.

### Development checkpoint 28 — reproducible offline photo mixed-load evidence gate

- Introduce a standalone `video one-shot assess-photo-load` CLI
  with **no PostgreSQL, FFmpeg, source-original, media, worker or
  cloud dependency**. It accepts three bounded local JSON files:
  photo-only measurements, genuinely observed selected-video mixed
  measurements, and explicit operator-reviewed percentage limits.
- Strict numeric-only input requires elapsed windows 60–3600 seconds,
  actually completed photo jobs, 20–200 latency observations for
  Visual, Caption, Combined search and slideshow advance in each
  phase. Invalid/missing metrics, wrong phase, wrong observed-video
  flag, extraneous source paths/fields, NaNs and invalid limits are
  rejected rather than silently scored.
- The tool produces per-minute photo job throughput and nearest-rank
  per-mode p95 regression comparisons against the **operator-supplied**
  maximum throughput drop and p95 increase. WI-0150/ADR-0012 does
  **not** define accepted numeric thresholds in the repo, so this
  change does not invent them or assert a passing regression.
- Output is source-free; even a within-limit calculation retains
  `diagnosticOnly=true`,
  `photoWorkloadIdenticalIndependentlyVerified=false`,
  `independentVideoProcessObservationVerified=false`,
  `videoExecutionAuthorized=false` and `wi0188Accepted=false`.
  Only reviewed field measurements can close the mixed-load gate.
- [Benchmark procedure and privacy contract](../../operations/video-photo-mixed-load-evidence.md)
  describes collecting matched offline samples, operator approval
  of thresholds **before** running a mixed trial, and the outstanding
  hard disk/IO/network boundary and cancellation/restart/exclusion
  proofs. This development PR **does not enable** a video worker,
  create a job, touch real videos or require any local benchmark yet.

**Remaining WI-0188 acceptance:** an explicitly reviewed disk/I/O,
filesystem privilege and network isolation policy (or separately
accepted bounded alternative); a real single-video controlled
encode and lease-fenced private derivative verification; concurrent
photo-job throughput and all search/slideshow p95 timings against
a fixed photo-only baseline; cancellation, pause, exclusion,
restart/retry and unexpected termination with clean recovery;
and independent post-trial original SHA-256/no hydration evidence.
Production activation remains prohibited until reviewed.

### Development checkpoint 29 — sampled Windows Job Object I/O transfer cutoff

- The Windows native Job Object resource profile now optionally requires
  paired **read and write transfer-byte ceilings** (each 1 MiB–2 GiB).
  Missing one side, negative/unbounded limits and invalid pairs fail
  validation before the child is launched. Normal existing capped
  synthetic callers continue to work without opting into additional
  accounting.
- A supervised capped process queries **Windows Job Object extended I/O
  accounting** during execution and again **immediately after exit,
  before any independent output validation/private publication**.
  When an observed job-wide read/write counter is above its limit,
  execution fails closed with the allowlisted code
  `io-budget-exceeded`; missing/failed accounting is refused as
  `io-accounting-unavailable`, retaining kill-on-close, time bounds
  and owner-scoped scratch cleanup. Only aggregate read/write byte
  counts may be returned from successful supervisor calls.
- The selected **single-video** FFmpeg profile opts into
  512 MiB observed read / 384 MiB observed write limits; its
  independent FFprobe uses 512 MiB read / 32 MiB write limits.
  Existing maximum memory, CPU, process count, tool wall time,
  staged original bounds, private workspace ownership and no-recall
  logic are unchanged.
- Windows CI tests cover limits and paired-bound refusals, overflow,
  native Job Object accounting around a fully bounded test process,
  no surviving scratch, and source-free diagnostics. No real
  archive files or maintained PostgreSQL were touched in development.
- **Limitations remain explicit:** these are sampled total child
  I/O transfer counters, *not* kernel-enforced disk-size, peak IOPS,
  bandwidth throttling, safe executable filesystem ACLs, network
  egress denial, GPU isolation or all parent staging/publication
  I/O. The Windows 10 1607+ OS does not support the old
  `SetIoRateControlInformationJobObject` API. It must not
  be described as equivalent to full sandboxing.
- **No real-video execution authorization:** a future isolated
  container/volume and per-process permissions/egress policy,
  followed by photo-only versus mixed-load measurements and
  cancellation/exclusion/restart/post-original checks, remain
  necessary before WI-0188 can be accepted.
  See [Windows Job Object safety runbook](../../operations/video-worker-windows-resource-envelope.md).

### Development checkpoint 30 — native Windows creation mitigations, synthetic only (2026-10-22)

- Opt-in `WindowsVideoJobLimits.RequireChildProcessRestriction`
  (default `false`) adds the Windows
  `PROC_THREAD_ATTRIBUTE_CHILD_PROCESS_POLICY` deny-child-creation
  attribute to the existing native suspended process launcher. The
  initial additional executable-image and legacy-extension mitigations
  caused native `cmd.exe` DLL initialization failure (`0xC0000142`)
  in Windows CI; they were deliberately removed pending separate
  investigation rather than being described as compatible.
  The required child restriction must be installed **before** the
  tool process resumes; failure aborts launch. The native attribute
  buffer remains valid through
  the `STARTUPINFOEX` lifetime. Existing bounded Job Object, handle
  allowlist, sanitized environment, source authorization and IO
  counters are unchanged.
- **Nothing in the application, synthetic pilot or selected real-video
  one-shot opts in.** Windows CI demonstrated that even first-party
  `cmd.exe` fails at loader initialization (`0xC0000142`) with the
  restriction requested, before it can demonstrate child-process
  denial. The policy stays an inactive opt-in for compatibility
  investigation only. Tests confirm that normal Job Object single
  and nested commands work, and treat mitigated startup failure as
  *incompatibility*, not evidence of secure denial.
- The restricted child-process setting is *defense in depth* and
  Microsoft states it is effective only in a sandbox which also
  denies elevated process handles, e.g. AppContainer. This is
  explicitly **not** low-privilege identity, hard disk/storage
  or bandwidth quota, network egress denial or archive access
  confinement. No unchanged real-video source was opened or queued.
- [Native mitigation operator experiment and next AppContainer design gate](../../operations/video-windows-native-mitigation-pilot.md)
  documents limitations and the optional **synthetic-only** host
  revalidation after CI and merge. WI-0188 remains `in_progress`.
  Real video trial, photo mixed-load measurements and post-trial
  integrity still require additional independent acceptance.

### Development checkpoint 31 — disposable synthetic AppContainer token/ACL feasibility (2026-10-22)

- Added **`video sandbox appcontainer-lab`** as a separate
  dry-run-by-default, Windows-only **synthetic text-file** experiment.
  The optional `--apply` does not touch video originals, codecs, the
  catalogue, OneDrive, derivatives, or the video processing queue.
  It requires the clean, marked OS-temporary-directory child previously
  used for the synthetic codec pilot and refuses arbitrary existing
  folders or user-selected executables and source paths.
- The new `WindowsVideoAppContainerProfile` creates a unique
  disposable Windows Package SID with **zero requested capabilities**.
  The lab creates owned working, allowed-input and blocked-input
  directories, grants this Package SID explicit working-write and
  generated-input-read ACLs, and grants only noninheriting traversal
  on the lab root. The sibling negative-test input has **no grant**.
  All generated files are transient fixed strings, not media.
- The existing native suspended launcher supports a **separate explicit
  AppContainer security-capabilities** attribute. It checks
  `TokenIsAppContainer=1` on the still-suspended child token
  **before** assigning/resuming. A silently ignored security
  attribute fails closed; no unrestricted fallback exists. The
  independent Windows Job Object still enforces CPU, memory,
  process count, wall-clock time, owned scratch and sampled I/O caps.
  The selected real-video worker and FFmpeg/FFprobe remain unchanged.
- A Windows lab probe tests **both** generated-file access paths:
  permitted read+write must work and a second ungranted read must
  fail. It never claims a complete sandbox when OS process launch or
  ACL setup fails. Generated scratch/profile teardown is required;
  any cleanup failure invalidates the proof.
  CLI output uses only fixed codes/booleans without user filenames,
  AppContainer SID, profile name, hashes or Windows error messages.
- The synthetic CLI and tests explicitly preserve
  `networkEgressDenialVerified=false`,
  `independentAclAuditVerified=false`,
  `hardDiskIoQuotaVerified=false`, and
  `realVideoExecutionAuthorized=false`. Requesting zero network
  capabilities is NOT the same as independently verifying
  outbound socket denial. Failure of Windows host compatibility
  is reported as **unavailable**, not security success.
- [Operator procedure and remaining limits](../../operations/video-synthetic-appcontainer-feasibility.md)
  covers the optional local test after Windows CI, the unique
  profile/ACL cleanup and independent remaining acceptance:
  FFmpeg+FFprobe AppContainer compatibility, no network egress,
  private source/cache ACL audit, hard storage containment,
  one-video controlled encode, mixed photo priority metrics,
  cancellation/exclusion/restart and post-run original-byte proof.
  WI-0188 remains `in_progress`; production activation is blocked.


### Development checkpoint 32 — inspect generated AppContainer ACLs before launch (2026-10-22)

- The disposable AppContainer synthetic test now audits its own generated
  root, allowed input, blocked input and scratch DACLs before launching the
  Windows command interpreter. The unique Package SID must have only root
  traversal, read-only allowed-input access and working-directory modify;
  no inherited Package SID permission may reach the blocked sibling.
  Broad `ALL APPLICATION PACKAGES` and `ALL RESTRICTED APPLICATION PACKAGES`
  ACEs on these lab paths are refused.
- A misconfigured/overbroad ACL returns the nonpassing
  `sandbox-acl-audit-failed` status before any child process starts.
  The sanitized `syntheticAclAuditVerified` field distinguishes DACL
  inspection from the separate observed allowed/denied file-access probes.
  Reported `independentAclAuditVerified` remains false because no real
  archive/cache/tool locations were inspected.
- Windows CI exposed that restoring the original inherited root DACL
  after adding an AppContainer ACE may leave changed Windows `AI`
  (auto-inheritance) descriptor flags even when the ACE is removed. This
  is treated as a real nonpassing cleanup failure, not normalized away.
  The pilot now grants scope traversal only on a disposable
  `sandbox-scope` child, deletes that child and verifies the surviving
  marked root ACL is unchanged, without writing to that root's DACL.
- This is **not** production identity or a network boundary. FFmpeg/FFprobe
  binary and DLL loading, real cache/source ACLs, independent socket denial,
  disk limits, and an authorized single-video/mixed-load trial all remain
  gates to WI-0188 acceptance. No maintained PostgreSQL work or video
  job activation is requested; v37 host-migration requirements remain
  unchanged. See the [synthetic runbook](../../operations/video-synthetic-appcontainer-feasibility.md).



### Development checkpoint 33 — hard-stop real-media one-shot mutations pending isolation acceptance (2026-10-22)

- The documentation has consistently **prohibited** real-video one-shot
  enqueue/processing before reviewed filesystem, network and disk containment
  and mixed photo/video load acceptance. Previously, however, the CLI
  `--apply` path could be reached with a schema-rehearsal acknowledgement
  even though the hardening evidence is incomplete.
- The selected `video one-shot enqueue --apply` and
  `video one-shot run --apply` CLI handlers now refuse real-media mutation
  **before PostgreSQL or filesystem access** with a fixed sanitized
  `media-isolation-not-approved` response, nonzero exit and
  `dryRun=true`. The existing v37 rehearsal acknowledgement and checked
  FFmpeg hashes are not sufficient to bypass the refusal.
- Tests run each apply request with **no configured PostgreSQL variable**
  and verify no queued job ID, source access or publication, and a denied
  result on all hosts. Read-only operator previews, synthetic experiments,
  ordinary photos and database schema are unchanged.
- This is intentionally a code-level fail-closed activation gate, not a
  declaration that the sandbox is complete. It can be lifted only through
  another separately reviewed change after evidence of real-path ACL/tool
  compatibility, independently observed socket denial, bounded disk/IO,
  single-video/source-integrity checks and maintained-host mixed load.
  No operator database migration, video encode or backup is requested by
  this checkpoint. See [current one-shot runbook](../../operations/video-scoped-one-shot-v37.md).


### Consolidated functional checkpoint — shared archive processing (2026-10-22)

- Default-off Settings preferences and online-only opt-in, persisted separately
  from photo catalogue tables. Video queue and sanitized counts are visible.
- Existing archive worker runs all available photo work first; only then
  advances at most one supported video candidate via the fixed, bounded
  FFmpeg/FFprobe adapter. Shared Pause interrupts the video token, and
  disabling videos preserves queued work for future recovery.
- Already-local sources are preferred. Explicitly opted-in OneDrive retrieval
  is followed by a request to return **only app-initiated** hydrated originals
  to online-only. Never delete or unpin already-local originals.
- When previously inspected eligible videos are exhausted, the shared
  archive incrementally discovers and registers one new MOV/MP4 inside
  included coverage per cycle, always preferring local media and excluding
  blocked source copies before opening bytes. OneDrive-only discovery is
  opt-in and bound to one download at a time. Unsupported media is recorded
  but never silently transcoded; local verification remains mandatory.
- No schema change is requested beyond maintained v37. This checkpoint
  replaces the older real-media CLI hard-stop with guarded in-app execution
  subject to explicit Settings opt-in and archive start; direct operator
  one-shot `--apply` stays blocked.
- **Queued selection follow-up (PR #656):** queued online-only placeholders
  cannot block a later local video, even when hydration is disabled. Previously
  queued and newly inspected candidates are limited to current included
  folders, and serialized PostgreSQL claims re-check current durable coverage
  for the specifically selected job. Selection and claim have regression tests.
- **CI and maintained-host verification are pending.** Documentation and
  implementation are not acceptance by themselves.

## Completion notes

- Files changed: Core admission/identity contracts and tests; v34 schema,
  isolated PostgreSQL queue repository/tests; guarded restore rehearsal CLI;
  operator runbook and delivery tracking.
- Trade-offs: queue-only schema and explicit operator-only control methods
  before any worker/API; no original-file reads, transcode or background video work.
- Deferred work: process/resource-enforcing video worker, actor controls,
  safe artifact publication/cleanup and maintained-host benchmarks.
- Verification: checkpoint 1 CI passed in merged PR #600; checkpoint 2 was
  locally accepted by maintainer (all mandatory checks PASS, Videos library
  optional/N/A). No further v34 migration is required solely for checkpoint 3.
- Checkpoint 3: merged PR #605; queue-only operator status/cancel commands.
- Checkpoint 4: Windows process-supervisor and PostgreSQL lease fencing tests
  run in PR CI; no schema change or runtime processing activation.
- Checkpoint 4: merged PR #607 after corrected Windows cleanup tests passed.
- Checkpoint 5: merged PR #609 and photo-priority claim tests passed CI.
- Checkpoint 6: merged PR #611, v35 durable stop controls; the local
  backup and isolated v34 → v35 rehearsal gate is still pending.
- Checkpoint 7: merged PR #613; fail-closed execution preemption bridge.
- Checkpoint 8: merged PR #615; opt-in native Windows resource bounds.
- Checkpoint 9: merged PR #617; bounded suspended native Windows launch,
  lease-verified staged video input and conservative abandoned-scratch cleanup.
- Checkpoint 10: unregistered one-attempt dispatch and durable-only success
  report gate; derivative publisher, activation, OS disk/I/O isolation
  and mixed-load local acceptance remain before closing WI-0188.
- WI-0188 still requires a real resource-controlled worker and Windows
  mixed-photo/video verification before acceptance.
