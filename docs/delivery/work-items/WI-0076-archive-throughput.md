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
3. `ArchiveAnalysisCoordinator.StartAsync`/`ResumeAsync` creates a new `LocalInspectionJobHandler` for each invocation. `LocalInspectionJobHandler.CreateAsync` loads model manifests and constructs detector/embedder objects before processing, then disposes them after that invocation. With one job per invocation this may repeat expensive ONNX/model initialization for every image.
4. Exact-original safety checks can read and SHA-256 the same file repeatedly. `CollectionOriginalAccessService.GetStatusAsync` verifies local content; `OpenVerifiedAsync` verifies it again; metadata inspection uses the verified stream; `LocalInspectionJobHandler` computes SHA-256 again before decode; proxy/release status checks can trigger further verification reads.
5. The archive loop normally prepares one hydratable pending revision at a time even though the storage policy has an explicit `MaximumConcurrentOperations` limit.
6. Review-proxy and face-review derivative work is also advanced in small serial steps.
7. The active-loop 500 ms delay adds some latency but is unlikely to explain the majority of a ~36-second-per-image average.

These are hypotheses to measure, not permission to weaken safety checks.

## Metrics-only baseline slice — 2026-08-27

The maintainer selected a measurement-first reset of WI-0076 from current `main`. The earlier
session-reuse PR #200 remains unmerged and is deliberately **not** part of the baseline build.

This slice adds process-local, privacy-safe aggregate diagnostics without changing processing
semantics. It does not alter:

- analysis concurrency or one-job-per-advancement behavior;
- detector/embedder models, thresholds or profile identity;
- SHA-256 verification requirements;
- hydration admission, byte/concurrency limits or release ownership;
- retry/cancellation behavior; or
- original/proxy/derivative bytes.

The resettable diagnostics contract is:

```text
GET  /api/archive/diagnostics/throughput
POST /api/archive/diagnostics/throughput/reset
```

It aggregates stage count/total/average/max timing, selected event counters and full-file SHA-256
read count/bytes. Hash-read distribution exposes only aggregate subject count/average/max reads;
opaque asset/revision keys used for the calculation are never returned.

The instrumentation covers synchronization, OneDrive wait, source verification, metadata,
model-session initialization/lifetime, analysis source hashing, image decode, detection,
alignment/embedding, face/result persistence, review-proxy generation, face-review derivatives,
hydration/release requests and archive errors. Existing WI-0079 synchronization hash diagnostics
are reused rather than adding another scanner path.

The maintainer benchmark procedure is
[`docs/operations/archive-throughput-benchmark.md`](../../operations/archive-throughput-benchmark.md).
Run the local-original and online-only scenarios against the same fixed 100–200 image media set
before selecting the next optimization. PR #200's session-reuse idea is one candidate to reapply
and A/B test only after the baseline identifies session setup as material.

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
