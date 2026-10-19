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

## Objective

Schedule on-demand heavy video jobs without disrupting ordinary photo ingestion, recognition, captioning, search or slideshow use.

## In scope

- Default-off video enablement and separate visible queue for selected videos; existing photo workflows never silently queue full-video processing.
- Limits for concurrent video jobs, CPU/GPU/IO, memory, hydrated source bytes/time, derived frames and local disk; photo work has priority and can pause video processing.
- Cancellation, progress, restart/retry, bounded temp-file cleanup and resumable/idempotent work; avoid stale partial results.
- Revision/model/preset/policy version identifiers; invalidate evidence after source change or exclusion while keeping originals immutable.
- Explicit pause/stop, operator-visible failure diagnostics and aggregate resource metrics without logging personal media contents or paths.

## Out of scope

Proxy format implementation, caption/embedding inference, face assignment, cloud workers or a replacement photo-job framework.

## Acceptance criteria

- [ ] Video processing is off by default after upgrade and only runs when enabled and selected.
- [ ] Concurrent photo/video benchmark records photo job throughput and search/slideshow latency against a photo-only baseline with WI-0150-agreed bounds.
- [ ] Restart, cancellation, exclusions, online-only files and malformed video cannot leak partial derivatives or unlimited hydrated files.
- [ ] Work is independently pausable/observable and obeys configurable concurrency/resource limits.

## Verification requirements

Deterministic job lifecycle tests, plus real mixed-load CPU/GPU/IO and photo regression measurements.

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
- Checkpoint 7: explicit execution safety polling / subprocess preemption
  bridge and PostgreSQL lease probe; no new migration or active worker.
- WI-0188 still requires a real resource-controlled worker and Windows
  mixed-photo/video verification before acceptance.
