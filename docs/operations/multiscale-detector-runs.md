# Multi-scale detector runs

Use this procedure for the governed WI-0036 YuNet experiment after the completed confidence sweep established that threshold tuning alone is insufficient.

The experiment keeps the model, confidence, padding, source set and frozen face-level ground truth fixed while changing only the detector pipeline from a single full-image pass to a full-image pass plus deterministic overlapping tiles.

## Fixed candidate policy

The first governed candidate uses:

- detector model `yunet-2023mar-fp32`;
- embedder model `sface-2021dec-fp32`;
- confidence `0.9`;
- padding `0.25`;
- detector pipeline `full-image-plus-tiles`;
- tile size `1024` pixels;
- tile overlap `0.20`;
- global merge NMS threshold `0.30`; and
- the unchanged 100-photo M16 sample and frozen confidence-0.9 ground truth.

Confidence remains at `0.9` for the first multi-scale candidate so the result isolates the pipeline change. Do not combine threshold tuning and tiling in the same first experiment.

## Invariants

Keep these inputs unchanged:

- the exact staged filenames and source bytes;
- the private manifest metadata and countable-face rule;
- the frozen confidence-0.9 face-level ground truth;
- model revisions and model-file SHA-256 values;
- candidate-comparison IoU threshold;
- padding and all non-detector preprocessing; and
- the canonical reviewed catalogue, which must not receive experiment detections.

Use a new database, output directory and log. Preserve the run configuration JSON because it records confidence, pipeline mode, tile size, overlap and merge threshold.

## Step 1: process the isolated candidate

Run from Windows PowerShell:

```powershell
$repo = "C:\Kod\codex\Photo Identity Indexer"
$sample = "C:\PhotoIdentity\M16\sample"
$candidateRoot = "C:\PhotoIdentity\M16\runs\multiscale-090"
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
    --confidence 0.9 `
    --padding 0.25 `
    --detector-pipeline full-image-plus-tiles `
    --tile-size 1024 `
    --tile-overlap 0.20 `
    --merge-nms 0.30 2>&1 | Tee-Object -FilePath $candidateLog

if ($LASTEXITCODE -ne 0) {
    throw "The multi-scale candidate processing run failed."
}
```

Record the printed run ID. Confirm that every intended photo completed successfully:

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

## Step 2: compare with the frozen ground truth

Start the review application against the completed candidate catalogue while retaining the existing private detector-evaluation root:

```powershell
$evaluationRoot = "C:\PhotoIdentity\M16\private\evaluation-sessions"

$env:PhotoIdentity__DatabasePath = $candidateDb
$env:PhotoIdentity__DetectorEvaluationRoot = $evaluationRoot

Set-Location -LiteralPath "C:\PhotoIdentity\M16\review-app"
dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open `http://localhost:5080/detector-comparisons`, select the immutable `M16 confidence 0.9 baseline`, select the completed multi-scale run and create a comparison named `M16 multi-scale YuNet confidence 0.9`.

Comparison creation must verify the exact source filename set and every full source SHA-256 before review begins.

## Step 3: review and assess

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

## Step 4: decide the next governed work

- If the complete gate passes, stop detector experimentation, cancel WI-0037 and continue to WI-0038.
- If it fails, preserve the candidate evidence and continue to WI-0037 unless a narrowly governed follow-up multi-scale configuration is explicitly approved.
- Do not silently tune tile, overlap, merge or confidence values after seeing the result.
