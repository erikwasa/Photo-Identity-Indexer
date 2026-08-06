# Multi-scale detector runs

Use this retained procedure for the governed WI-0036 YuNet experiments that followed the completed confidence sweep.

The experiments kept the model, padding, source set and frozen face-level ground truth fixed while changing the detector pipeline from a single full-image pass to a full-image pass plus deterministic overlapping tiles.

## Fixed pipeline policy

Both governed candidates used:

- detector model `yunet-2023mar-fp32`;
- embedder model `sface-2021dec-fp32`;
- padding `0.25`;
- detector pipeline `full-image-plus-tiles`;
- tile size `1024` pixels;
- tile overlap `0.20`;
- global merge NMS threshold `0.30`; and
- the unchanged 100-photo M16 sample and frozen confidence-0.9 ground truth.

The first candidate used confidence `0.9` so the result isolated the pipeline change. A later confidence-0.7 follow-up was explicitly approved before processing after the first candidate failed and the earlier single-pass sweep showed that `0.7` and `0.6` had the strongest recall behavior.

## Invariants

The following inputs remained unchanged:

- the exact staged filenames and source bytes;
- the private manifest metadata and countable-face rule;
- the frozen confidence-0.9 face-level ground truth;
- model revisions and model-file SHA-256 values;
- candidate-comparison IoU threshold;
- padding and all non-detector preprocessing; and
- the canonical reviewed catalogue, which did not receive experiment detections.

Each candidate used a new database, output directory and log. Its durable run configuration records confidence, pipeline mode, tile size, overlap and merge threshold.

## Processing pattern

The following PowerShell pattern was used for each isolated candidate. Change only `$confidence` and `$candidateName` after recording the candidate decision.

```powershell
$repo = "C:\Kod\codex\Photo Identity Indexer"
$sample = "C:\PhotoIdentity\M16\sample"
$confidence = 0.9
$candidateName = "multiscale-090"
$candidateRoot = "C:\PhotoIdentity\M16\runs\$candidateName"
$candidateDb = Join-Path $candidateRoot "catalogue.db"
$candidateOutput = Join-Path $candidateRoot "outputs"
$candidateLog = Join-Path $candidateRoot "batch-start.log"

New-Item -ItemType Directory -Force `
    -Path $candidateRoot,$candidateOutput | Out-Null

if (Test-Path -LiteralPath $candidateDb) {
    throw "Candidate catalogue already exists: $candidateDb"
}

Set-Location -LiteralPath $repo

& dotnet run `
    --project .\src\PhotoIdentity.Cli `
    -- `
    batch start `
    --database $candidateDb `
    --source $sample `
    --output $candidateOutput `
    --detector-model yunet-2023mar-fp32 `
    --embedder-model sface-2021dec-fp32 `
    --confidence $confidence `
    --padding 0.25 `
    --detector-pipeline full-image-plus-tiles `
    --tile-size 1024 `
    --tile-overlap 0.20 `
    --merge-nms 0.30 2>&1 | Tee-Object -FilePath $candidateLog

if ($LASTEXITCODE -ne 0) {
    throw "The multi-scale candidate processing run failed."
}
```

Record the printed run ID and confirm that every intended photo completed successfully:

```powershell
$runId = "REPLACE_WITH_PRINTED_RUN_ID"

dotnet run `
    --project .\src\PhotoIdentity.Cli `
    -- `
    batch status `
    --database $candidateDb `
    --run $runId
```

Resume the same durable run after an interruption rather than creating another candidate:

```powershell
dotnet run `
    --project .\src\PhotoIdentity.Cli `
    -- `
    batch resume `
    --database $candidateDb `
    --run $runId
```

## Comparison and review

Start the review application against the completed candidate catalogue while retaining the existing private detector-evaluation root:

```powershell
$evaluationRoot = "C:\PhotoIdentity\M16\private\evaluation-sessions"

$env:PhotoIdentity__DatabasePath = $candidateDb
$env:PhotoIdentity__DetectorEvaluationRoot = $evaluationRoot

Set-Location -LiteralPath "C:\PhotoIdentity\M16\review-app"
dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open `http://localhost:5080/detector-comparisons`, select the immutable `M16 confidence 0.9 baseline`, select the completed multi-scale run and create a comparison with a name that includes the exact confidence.

Comparison creation must verify the exact source filename set and every full source SHA-256 before review begins.

Resolve only surfaced unmatched, duplicate or ambiguous cases. Record the material-category assessment, then export the summaries.

The unchanged M16 gate is:

- overall recall at least `90%`;
- five-plus-face recall at least `85%`;
- no more than `10` false or duplicate detections; and
- no material failure category incompatible with the archive workflow.

Retain privately:

- overall and five-plus recall;
- false and duplicate counts;
- category evidence;
- candidate processing duration;
- exception-photo and manual-decision counts as review-effort evidence;
- the candidate database, output, log and comparison file; and
- the exported comparison summary.

Commit only privacy-safe aggregate pass/fail and workflow conclusions.

## Final WI-0036 results

The maintainer completed the governed comparisons on 2026-08-07.

### Multi-scale confidence 0.9

- Failed the complete M16 gate.
- Performed better than the single-pass confidence-0.9 baseline and the single-pass confidence-0.8 candidate.
- Demonstrated that tiling recovered useful faces, but not enough to approve the pipeline.

### Multi-scale confidence 0.7

- Returned more than 100 false or duplicate detections across the fixed sample.
- Failed the gate decisively against the maximum of 10.
- Did not justify further review or a confidence-0.6 run.

No confidence-0.6 multi-scale candidate was processed. Lowering confidence further could not plausibly repair the already disqualifying false/duplicate workload.

## Final decision

WI-0036 is complete and no YuNet multi-scale configuration is approved for rollout. Preserve the private experiment evidence and continue to [WI-0037](../delivery/work-items/WI-0037-detector-candidate.md).

The opt-in multi-scale implementation remains available for reproducibility and future research, but it must not become the canonical detector without a new governed decision.
