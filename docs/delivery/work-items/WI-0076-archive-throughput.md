---
id: WI-0076
title: Improve archive processing throughput
milestone: M20
status_source: ../status/work-items.yaml
depends_on: [WI-0042, WI-0072]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Worker, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Source.OneDriveSync]
---

# WI-0076: Improve archive processing throughput

## Objective

Measure and materially improve permanent-archive processing throughput from the observed baseline of roughly **100 images/hour**, while preserving immutable revision verification, bounded OneDrive hydration, restart/resume behavior and deterministic analysis results.

Do not begin by increasing concurrency blindly. First identify where wall-clock time is spent and remove repeated setup/I/O before adding controlled parallelism.

## Current architectural observations

Repository inspection identifies several high-value hypotheses:

1. `ArchiveBoundedAnalysisService` deliberately serializes advancement through a one-slot semaphore and advances at most one governed source-verification, metadata, analysis or post-analysis step at a time.
2. Both new and resumed analysis use `ResumableBatchProcessorOptions(maxAttemptsPerInvocation: 1)`, effectively processing one analysis job per bounded advancement invocation.
3. Before the first WI-0076 optimization slice, `ArchiveAnalysisCoordinator.StartAsync`/`ResumeAsync` created a new `LocalInspectionJobHandler` for each invocation. `LocalInspectionJobHandler.CreateAsync` loads model manifests and constructs detector/embedder objects. With one job per invocation this repeated expensive ONNX/model initialization for every image.
4. Exact-original safety checks can read and SHA-256 the same file repeatedly. `CollectionOriginalAccessService.GetStatusAsync` verifies local content; `OpenVerifiedAsync` verifies it again; metadata inspection uses the verified stream; `LocalInspectionJobHandler` computes SHA-256 again before decode; proxy/release status checks can trigger further verification reads.
5. The archive loop normally prepares one hydratable pending revision at a time even though the storage policy has an explicit `MaximumConcurrentOperations` limit.
6. Review-proxy and face-review derivative work is also advanced in small serial steps.
7. The active-loop 500 ms delay adds some latency but is unlikely to explain the majority of a ~36-second-per-image average.

These are hypotheses to measure, not permission to weaken safety checks.

## Implementation progress

### Slice 1 — reuse detector/embedder sessions across bounded advancements

The first optimization keeps the governed one-job-per-advancement behavior unchanged and removes repeated model setup instead of adding concurrency.

`ArchiveAnalysisCoordinator` now shares one `LocalInspectionJobHandler` session for the lifetime of the active catalogue database. Consecutive coordinator instances therefore reuse the same detector/embedder session across `StartAsync`/`ResumeAsync` calls when the exact analysis profile and full batch configuration match.

Safety boundaries:

- the cache key includes the exact analysis profile hash plus the serialized batch configuration, so a model/profile change or runtime-path/configuration change disposes the old handler and creates a new compatible session;
- a per-catalogue semaphore prevents two coordinator invocations from using the same detector/embedder session concurrently;
- durable processing checkpoints, one-job invocation limits, metadata-before-analysis behavior, SHA-256 verification and retry semantics are unchanged in this slice;
- no new parallelism, hydration prefetch or broad retry behavior is introduced.

Lightweight in-process diagnostics track session generation/initialization count, cumulative attempts processed and the latest model-session initialization duration through `ArchiveAnalysisCoordinator.GetSessionDiagnostics()`. The representative maintainer benchmark remains required to quantify the actual throughput improvement on the archive hardware.

The next WI-0076 slice should use that benchmark to decide whether the dominant remaining cost is full-file verification I/O, image inference, OneDrive wait or the fixed one-job/one-hydration sequencing before batching or prefetch is introduced.

## Investigation slice

Add lightweight timing/counter evidence for a representative archive run, including at least:

- synchronization/catalogue scan;
- OneDrive hydration wait;
- source verification/hash;
- metadata inspection;
- analysis handler/model creation;
- image decode;
- face detection;
- alignment/embedding and face persistence;
- review-proxy generation;
- face-review derivative generation;
- release request/wait;
- count/bytes of full-file SHA-256 reads per revision.

Capture CPU utilization, effective active model session lifetime and whether the pipeline is predominantly CPU-, storage-, hash-I/O- or OneDrive-bound.

## Optimization priorities

Implement the safest high-impact changes supported by measurements. Preferred order:

### 1. Reuse expensive analysis resources / process batches

- Avoid constructing detector/embedder sessions for every single image when one archive run can safely reuse them.
- Allow a bounded configurable number of ready local jobs to run per analysis invocation while keeping durable per-job checkpoints and cancellation semantics.
- Keep model/profile identity checks exact; a reused handler must never cross an incompatible analysis profile.

### 2. Reduce redundant full-file reads without weakening integrity

- Identify repeated SHA-256 verification of the same unchanged local revision within one governed processing lifecycle.
- Introduce a safe verified-local lease/context or equivalent mechanism if it can prove the same exact revision across metadata/analysis/proxy steps.
- Do not replace immutable content verification with pathname trust or a long-lived unsafe cache.

### 3. Bounded hydration prefetch

- When policy permits, request a small window of upcoming online-only revisions so OneDrive transfer can overlap CPU analysis of already-local revisions.
- Respect `MaximumConcurrentOperations`, maximum managed bytes, free-space reserve and release ownership.
- Never hydrate arbitrary future content outside the configured archive scope.

### 4. Batch/overlap derivative work where safe

- Evaluate processing review proxies/face-review derivatives in bounded batches while an original is already local, reducing repeated status/open/release cycles.
- Keep UI requests responsive and avoid monopolizing SQLite or large-read admission.

### 5. Loop-delay tuning

- Only after larger bottlenecks are addressed, consider reducing/avoiding fixed active polling delay when immediately runnable work exists.

## Acceptance criteria

- [ ] A controlled before/after benchmark is recorded on the same representative media set and hardware.
- [ ] Stage timings identify the dominant contributors to the original ~100 images/hour baseline.
- [ ] Model/session creation frequency is measured and repeated per-image initialization is removed or justified.
- [ ] Full-file verification/hash-read count per revision is measured and avoidable duplicate reads are removed without weakening revision safety.
- [ ] If hydration is material, bounded prefetch overlaps transfer with useful work while all byte/concurrency/reserve limits remain enforced.
- [ ] Processing remains restart/resume safe and idempotent after interruption.
- [ ] Existing analysis profile hashes/results and review evidence semantics remain unchanged.
- [ ] No broad automatic retry behavior is introduced.
- [ ] Archive UI remains responsive while a large run is active.
- [ ] The resulting throughput improvement and any new tuning settings are documented for operators.

## Verification

Use a representative mixed JPEG/HEIC archive sample large enough to amortize startup costs (preferably at least 100 images). Record total elapsed time, images/hour, stage timings, peak managed hydration bytes and failures before and after each material optimization so improvements are attributable rather than anecdotal.
