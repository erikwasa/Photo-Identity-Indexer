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

## Maintainer benchmark evidence — 2026-08-28

The metrics-only PR was exercised against the same private 155-image corpus on the maintainer machine.

### Scenario A — originals already local

- 155 images analysed successfully with 0 failures.
- Sample wall clock: **25m 53s**, or **359.24 images/hour** and **10.02 seconds/image**.
- 155 analysis attempts created **155 model sessions**.
- Model-session initialization consumed about **1,279 seconds total**, averaging about **8.25 seconds/image**.
- This was the dominant local cost.
- 741 full-file SHA-256 reads were observed across the 155 revisions, but local hash time was small relative to model setup.
- The fixed active-loop delay contributed about 0.5 seconds/image.

### Scenario B — the same originals online-only

The 155th image reached analysed state after a sampled processing phase of **39m 22.19s**, or
**236.22 images/hour** and **15.24 seconds/image**, with 0 analysis failures.

The run did not terminate normally after analysis. It was deliberately paused after an additional
**44m 51.37s** post-analysis stall so the evidence could be preserved without manually opening,
pinning or otherwise contaminating the source media.

At the stall:

- 155 images were analysed, 0 pending and 0 failed;
- 154 originals had returned to online-only;
- one 5,539,796-byte managed original remained in `downloading`;
- the stuck revision was already source-verified and analysed;
- counters showed 155 analysis attempts, 155 model-session initializations, 155 release requests,
  but **156 hydration requests**;
- no face-review derivative completion had been recorded;
- model-session initialization consumed **1,487.75 seconds total**, averaging **9.60 seconds/image**;
- the useful analysis-session lifetime averaged only **0.92 seconds/image**;
- 775 full-file hash reads were observed, exactly five per revision in this scenario
  (source verification once, original-open once, original-status twice, analysis once);
- 5,163 active-loop delays consumed about **43m 37s**.

The extra hydration plus the pending face-review derivative path identifies a separate liveness /
ordering defect: review-proxy generation releases a managed original before the independent
face-review derivative backfill has consumed it. Backfill then rehydrates an already-analysed
revision. If that hydration remains in progress, the derivative is still classified as runnable work,
so advancement reports `running` and accrues the 500 ms active delay instead of reporting
OneDrive `waiting`.

Consequences for WI-0076:

1. Fix post-analysis derivative ordering/liveness first so an online-only benchmark can terminate
   cleanly. Prefer keeping a managed original hydrated through review-proxy and face-review
   derivative generation before release, and classify a derivative blocked on hydration as waiting.
2. Then reapply and benchmark the session-reuse concept from PR #200 against current code.
   Scenario A and B both show per-image model initialization as the dominant throughput cost.
3. Defer duplicate-hash reduction until after the larger measured costs are removed; the repeated
   reads are real but comparatively cheap on the maintainer's local storage.

## Derivative liveness slice — 2026-08-28

After PR #210 merged, the first optimization slice addresses the Scenario B liveness defect without
changing analysis concurrency or model-session behavior.

The bounded post-analysis path now keeps a managed original local through both durable consumers:

```text
review proxy -> face-review derivative -> release
```

This removes the avoidable release/rehydrate boundary that produced the 156th hydration request in
the 155-image online-only benchmark. The same ready-revision derivative generation is also applied
before the separate verified-managed release path so older catalogues with an existing proxy cannot
release a revision while its face-review derivative still needs the original.

Legacy derivative backfill remains available for already-analysed catalogues. When its pending
revision is downloading or releasing, that pending work is excluded from runnable CPU work and is
reported explicitly as OneDrive-blocked work. The advancement classifier therefore reports
`waiting` when no other runnable work remains instead of accruing the 500 ms active-loop delay.

This slice deliberately does **not** apply PR #200 session reuse. After it passes CI, rerun Scenario B
with a fresh disposable catalogue. A clean run must reach `complete`, settle managed hydration
ownership, avoid the extra post-analysis rehydration cycle and record face-review derivative
completion. Session reuse is the next separate slice after this liveness baseline is clean.

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
