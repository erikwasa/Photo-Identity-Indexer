# WI-0190 checkpoint 13 — representative private frame-evidence acceptance report

The selected one-shot already published and audited **one** real local
two-frame clip. Checkpoints 9–12 added human-annotated scene coverage,
actual scene-balanced selected publication and private retrieval-quality
scoring. The remaining WI-0190 acceptance needs **comparable evidence
across multiple different representative private videos**, including
storage, scene omissions, caption judgments, throughput and CPU/GPU/
Azure cost. This checkpoint adds a consolidated **read-only** audit.

The command does **not** produce frames or infer cost. It consumes a
**private operator-authored** local JSON manifest specifying opaque
FrameEvidence job UUIDs, descriptive **fixed-category labels**, numeric
human-annotated scene cuts and **optional numeric measurements**.
It reads only frame metadata and timestamp positions from **already
published, current** local PostgreSQL v38 batches. It does **not**
fetch captions, embeddings, source names, original bytes, published
MP4, CLIP models or images; it never calls FFmpeg, Ollama or Azure.

Every selected job is verified against the same checks as
`video frames audit`: successful complete batch, latest local-folder
video revision, supported SDR, no exclusion, valid timestamps and
vector byte lengths. **All jobs share one PostgreSQL repeatable-read,
READ ONLY transaction**. A second job for the *same video asset*
is refused; alternate sampling variants do not count as a second
representative clip. If any selected batch is stale, partial,
excluded or unsupported, **the entire report fails** and prints
no per-clip data or partial results.

## Private JSON manifest

Store the manifest under a protected physical directory inside
`$env:LOCALAPPDATA`, outside Git, OneDrive, cloud-synced folders
and shared drives. The following UUIDs and numeric data are
**illustrative**; do not claim these are real measured results.

~~~json
{
  "protocol": "wi0190-acceptance-evidence-v1",
  "cases": [
    {
      "jobId": "00000000-0000-0000-0000-000000000001",
      "categories": ["short", "multi-scene"],
      "sceneCutsMs": [3000, 6000, 9000],
      "executionWallMs": 123456,
      "cpuTimeMs": null,
      "gpuTimeMs": null,
      "azureCostMinorUnits": null,
      "captionsJudged": 2,
      "captionsAccurate": 1
    },
    {
      "jobId": "00000000-0000-0000-0000-000000000002",
      "categories": ["long", "rotated", "low-light"],
      "sceneCutsMs": null,
      "executionWallMs": null,
      "cpuTimeMs": null,
      "gpuTimeMs": null,
      "azureCostMinorUnits": null,
      "captionsJudged": null,
      "captionsAccurate": null
    }
  ]
}
~~~

Use actual separate FrameEvidence jobs for different video assets.
Fixed categories are `short`, `multi-scene`, `long`,
`rotated` and `low-light`. Labels are *operator classifications*,
not video properties verified by SQL. A clip may have more than one
category. A report checks which classes are represented, **not**
whether their sample count or distribution is statistically adequate.

`sceneCutsMs`: null means **not annotated**; an empty array means the
operator judged the clip to have **one scene**. Nonempty positions are
strictly increasing integer milliseconds inside the inspected video,
up to 128 cuts. Actual covered/missed scenes are calculated from
persisted sampled timestamps using the existing `video frames
coverage` policy. Scenes not annotated cannot be detected.

`executionWallMs`: optional numeric duration from that exact
selected one-shot's `executionWallMs` report, including its disarm
stage. This field is **not derived** from database timestamps.
`cpuTimeMs` and `gpuTimeMs`: optional externally instrumented,
per-clip cumulative processor/GPU times; do not fill these from wall
time or an estimate. `azureCostMinorUnits`: optional actual cost
allocated to this clip in the **minor currency units** of the
operator's billing currency (for example öre or cents). **Every
observation in a single report must use the same billing currency**;
the evaluator cannot independently prove billing, currency or
allocation. Zero is an observed zero, and null is unmeasured.

`captionsJudged` and `captionsAccurate` must both be supplied or
both be null. They represent the number of **manually inspected**
persisted frame captions and how many meet the operator's consistent
correctness rubric. The judged count cannot exceed that job's actual
frame count; no caption text is retrieved by this command.
This is binary caption agreement, **not** retrieval relevance quality.
Use checkpoint 12's `video frames evaluate-live` separately for
human-judged multi-video retrieval relevance.

## Invocation

~~~powershell
git switch main
git pull --ff-only origin main

$manifest = Join-Path $env:LOCALAPPDATA "PhotoIdentity\PrivateEvaluation\acceptance-cases.json"
$acceptanceArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "frames", "acceptance-report",
    "--postgres-connection-env", "PhotoIdentity__Postgres__ConnectionString",
    "--database", "photoidentity",
    "--manifest", $manifest
)
$report = dotnet @acceptanceArgs
$exitCode = $LASTEXITCODE
$report
"Exit code: $exitCode"
~~~

The command accepts 1–32 distinct jobs, at most 24 frames/job
and a private local JSON up to 64 KiB. Unknown JSON fields, remote
paths, linked files/parents, non-monotonic scene cuts and invalid
metrics are refused. The report is a numeric summary only:

- Frame count, total/mean **caption + embedding payload bytes** per
  current clip, minimum and maximum inspected duration. This excludes
  PostgreSQL row/header/index/WAL overhead and proxy storage.
- A five-category count and explicit `missingCategories`. Those
  categories are self-reported and do not establish video-property
  truth.
- Annotated clip count and scene-weighted covered/missed scene counts.
  **Unknown** coverage is null, never silently counted as full.
- Count and sum of clips with recorded wall, CPU, GPU and Azure costs,
  and observed frames/minute **only for wall-timed clips**. The
  output does not assume resource use was measured on uninstrumented
  clips. Cross-clip cost averages are deliberately omitted.
- Number of judged captions and manually judged correctness rate,
  only where observations were supplied.

`humanObservationsSelfReported=true`,
`coverageFromOperatorAnnotatedCutsOnly=true`, and
`fullAcceptanceEstablished=false` prevent the output from being
mistaken for formal WI acceptance. This is an evidence *summary*,
not a replacement for reviewer judgment of representative inputs,
false captions, missed scenes or costs. The CLI's error messages
are sanitized; no private UUID, cut coordinate, name or manifest
content should appear in reports or CI logs.

### Why missing values are not zero

A successful report from just the previously accepted 12.3s clip
would show `clips=1`, several `missingCategories`,
and potentially **null** scene coverage/CPU/GPU/cost.
That would be a valid **partial baseline** but would *not*
meet criterion 3 or 4. It should not provoke a repeat of the
previously accepted interval audit.

### Verification and remaining acceptance

The PR provides pure aggregation unit tests and disposable PostgreSQL
integration tests: complete current rows, per-clip annotated scene
coverage, no caption/identity disclosure, stale newer-revision
refusal and exclusion refusal. No personal media or production
database access is needed to validate CI.

A **consolidated maintained-Windows acceptance session** is still
needed once representative supported clips are selected and
individual explicit frame jobs have been approved: classify the
five clip types, annotate numeric cuts, record the existing
one-shot wall times, separately instrument CPU/GPU and Azure
billing, judge captions and run multi-video retrieval
`evaluate-live`. Keep private JSON/judgments on the maintained
host. Also test real interruption, process shutdown and scratch
cleanup. **No new operator activity is necessary merely to review
this PR**, and WI-0190 stays `in_progress`, with formal
criteria **0/4** complete.
