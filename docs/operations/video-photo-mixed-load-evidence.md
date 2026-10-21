# WI-0188 — photo-only vs selected-video mixed-load evidence contract

**Implementation checkpoint only — no execution authorization.** A production
video lane cannot be accepted merely because the Windows FFmpeg synthetic pilot
and the original video checksum passed. WI-0188 specifically requires photo
job throughput, search latency and slideshow responsiveness under competition
with a deliberately selected video process, against an equivalent photo-only
baseline. The accepted WI-0150/ADR-0012 architecture provides the priority
contract but does **not** define final numerical latency/throughput
regression limits. Do not invent them.

## Accepted before this development slice (2026-10-21)

The maintained operator confirmed all of the following with **PASS**:
PostgreSQL backup, isolated restore, v34→v37 rehearsal and preserved
catalogue counts; schema-v37 health and default-off/paused/unselected
video control; existing photo, face, search, collections, metadata,
slideshow and Videos library/poster regressions; no unintentional
OneDrive hydration/original changes; independent FFmpeg/FFprobe
landscape-and-portrait synthetic validation; one already-local
supported SDR revision (12.3 seconds, 21,687,565 bytes), exact
`one-shot enqueue` dry-run eligibility, separate private-cache/tool
environment preflight and, finally, optional explicit
`one-shot source-proof --read-local-bytes` verification:
`status=verified`, `revisionDigestMatched=true`,
`sourceMetadataStable=true`, with no source write, hydration
request, enqueue or video execution.

**These proofs remain accepted.** They do not establish hard
disk/I/O/network sandboxing, real video processing, post-run source
integrity, or concurrent photo workload performance. WI-0188 remains
`in_progress`.

## Offline evaluator — no PostgreSQL or video process

New command:

~~~powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- `
  video one-shot assess-photo-load `
  --baseline-file $baselineReport `
  --mixed-file $mixedReport `
  --limits-file $operatorLimits
~~~

All three input paths must be explicit, existing absolute **local**
JSON files (maximum 64 KiB, no linked files); `--apply` and extra
flags are rejected. No database connection, archive source path,
file hashing, web calls, cloud hydration, FFmpeg or worker action
exists in the command. It only reads these three *operator-provided*
JSON files, evaluates fixed numerical fields and returns an
aggregate, source-free summary.

### Phase observations (two JSON files)

A strictly formatted photo-only baseline file looks like:

~~~json
{
  "protocol": "wi0188-photo-load-observation-v1",
  "phase": "photo-only",
  "photoJobsCompleted": 0,
  "windowSeconds": 0,
  "videoJobObserved": false,
  "visualSearchMs": [],
  "captionSearchMs": [],
  "combinedSearchMs": [],
  "slideshowAdvanceMs": []
}
~~~

A mixed-load file has the **exact same fields**, with
`"phase": "mixed-with-video"` and
`"videoJobObserved": true` only when **the operator has independently
observed the specifically approved video job actually executing**
during the measurement. Do **not** set that value from an intended
future trial or an FFmpeg *synthetic* fixture. The numerical zeros and
empty arrays above are **deliberate incomplete placeholders**, NOT
valid benchmark evidence or example performance claims.

To obtain evaluable observations, for **each** phase:

- Run the *same predeclared photo workload* (source scope, concurrency,
  image-analysis activities, searches, Smart/Creative settings,
  slideshow collection and machine state), while the photo job counts
  are measured over a fixed 60–3600-second window.
  `photoJobsCompleted` must be the number of jobs that actually
  reached successful completion during that window (1–100000),
  not the number requested or queued. Compare normalized
  `photoJobsCompleted/windowSeconds × 60`.
- Measure at least 20 and no more than 200 **individual** response
  times in milliseconds, each finite, positive and at most 120000,
  for all four modalities: Visual, Caption, Combined Search,
  and slideshow advance. Use equivalent queries and photo
  selection/order in both phases. Record numbers **without**
  query text, URLs, archive paths, filenames, person names, photo
  content or source IDs. Use the same client, warmup policy, browser
  viewport and location for both phases.
- The mixed trial itself must remain a *separately authorized*
  one-job video attempt; this offline evaluator can neither
  schedule that trial nor attest it occurred. Start with
  photo-only baseline capture before authorizing any video
  process. If no genuinely completed photo jobs were exercised
  or a modality has fewer than 20 measurements, mark the test
  *incomplete* rather than using placeholder numbers.
- Do not use differing photo workloads, unobserved hidden background
  runs, cloud hydration, or samples from different machines to
  claim evidence of priority isolation. Keep the raw timing and
  workload setup in private local files only.

The evaluator applies the **nearest-rank** p95 statistic separately
to the four latency arrays (19th sample in a 20-sample ordered
set). It computes throughput and p95 regression percentages
relative to the baseline; improvements are counted as **zero**
regression, not hidden by an average. It rejects unknown fields
so an accidental archive pathname or query cannot be ingested
or copied into report output.

### Operator-supplied comparison limits (third file)

~~~json
{
  "protocol": "wi0188-photo-load-operator-limits-v1",
  "maxThroughputDropPercent": 0,
  "maxP95IncreasePercent": 0
}
~~~

These zero values are **placeholders**, not accepted thresholds.
Replace them with *reviewed*, explicit real limits in the 0–100%
range **before** gathering mixed-load acceptance data. Until the
maintainer approves thresholds and the measured photo-only
baseline, the benchmark gate is unresolved. Do not tune the
limits after observing the mixed run to make a failing result pass.

The offline output contains `withinOperatorLimits` and five
numerical metrics; an exit code of 1 means at least one metric
regressed beyond the supplied limits. Successful comparison
always retains:

~~~json
{
  "diagnosticOnly": true,
  "evidenceProvenance": "operator-supplied",
  "photoWorkloadIdenticalIndependentlyVerified": false,
  "independentVideoProcessObservationVerified": false,
  "videoExecutionAuthorized": false,
  "wi0188Accepted": false
}
~~~

The fields above are a **partial explanation of the result**, not
a complete output sample. The evaluator cannot prove that
workloads truly matched or that a real video process executed:
human review and separate Windows resource/containment logs
remain prerequisites. Neither benchmark math nor a passing
photo regression number replaces proper exclusion, restart,
cancellation, source-integrity **post-trial** and no-hydration
proofs.

## Remaining engineering gates before a real video trial

1. Review an **enforceable** local filesystem/disk allocation and
   read/write I/O risk boundary (separate dedicated volume/low
   privilege/OS quota or another explicitly accepted controlled
   experiment). Scratch-entry polling and preflight free space
   alone are **not** OS quotas. A Windows Job Object CPU hard cap
   and memory limit do not enforce disk or network isolation.
2. Review process identity/permissions and no network egress
   policy for untrusted media parsers; FFmpeg's fixed file-only
   argument profile does **not** itself prove kernel network denial.
3. Approve the exact one-video trial, private cache volume,
   dedicated original pre/post SHA-256 protocol, photo-workload
   baseline, observed resource counters, cancellation/preemption
   scenarios and operator abort procedure. No mass job scheduling.
4. Run the authorized job only after these gates; then capture
   matched mixed-load numbers and independently validate private
   derivatives, video control disarm, cleanup, originals and photo
   regressions. Final WI-0188 acceptance is a separate decision.

Do **not** redo the already accepted v34→v37 migration rehearsal,
synthetic codec test, photo baseline compatibility, or source
pre-trial proof merely for this offline evaluator.
