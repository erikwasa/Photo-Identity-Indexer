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
- Checkpoint 3 validation: CI compile, disposable PostgreSQL tests and Docs check
  before merge; no new schema or runtime processing activation.
