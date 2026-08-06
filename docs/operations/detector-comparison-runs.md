# Detector comparison runs

Use this procedure for the governed M16 confidence sweep after WI-0039 and WI-0040 have been merged. The completed confidence-0.9 baseline session and frozen face-level ground truth must remain available in the private detector-evaluation root.

Each candidate uses an isolated catalogue and the unchanged 100-photo evaluation set.

## Current governed status

- Confidence `0.9`: immutable baseline; fully reviewed; failed the M16 gate.
- Confidence `0.8`: isolated candidate; fully reviewed on 2026-08-05; failed the M16 gate.
- Confidence `0.7`: next candidate.
- Confidence `0.6`: run only if still required.
- Confidence `0.5`: run only if still required.

Do not rerun completed candidates. Preserve their databases, logs, outputs, private comparisons and exports as durable evidence.

## Invariants

Keep these inputs unchanged for every candidate:

- the exact 100 staged filenames;
- the source bytes for every photo;
- the private manifest metadata and countable-face rule;
- the frozen confidence-0.9 face-level ground truth;
- the IoU (intersection over union) threshold, which defaults to `0.50`;
- the detector and embedder model revisions;
- the padding ratio, which defaults to `0.25`; and
- all other preprocessing configuration except for the confidence value being evaluated.

Comparison creation verifies the complete filename set and full SHA-256 revision hash for every source photo. A changed, missing, extra or duplicate source prevents comparison creation.

## Step 1: confirm the frozen baseline

The baseline ground truth is frozen once. Do not create a new baseline snapshot for each candidate.

Run the application against the completed confidence-0.9 baseline catalogue and the existing private detector-evaluation root only when the snapshot still needs to be created or verified:

```powershell
$publish = "C:\PhotoIdentity\M16\review-app"
$baselineDb = "C:\PhotoIdentity\M16\runs\confidence-090\catalogue.db"
$evaluationRoot = "C:\PhotoIdentity\M16\private\evaluation-sessions"

$env:PhotoIdentity__DatabasePath = $baselineDb
$env:PhotoIdentity__DetectorEvaluationRoot = $evaluationRoot

Set-Location -LiteralPath $publish
dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open:

```text
http://localhost:5080/detector-comparisons
```

Select `M16 confidence 0.9 baseline`. If no frozen snapshot exists, choose **Freeze reusable ground truth**. Freezing succeeds only when all 100 baseline photos are complete and their arithmetic still matches:

```text
countable_faces = correct_or_background_detections + manually_marked_misses
```

The snapshot is stored privately under:

```text
<DetectorEvaluationRoot>\ground-truth
```

After the snapshot exists, stop the application before switching catalogues. Candidate catalogues do not need the baseline processing run, but every review session must use the same detector-evaluation root.

## Step 2: process one confidence in an isolated catalogue

Use separate paths, for example:

```text
C:\PhotoIdentity\M16\runs\confidence-080\catalogue.db
C:\PhotoIdentity\M16\runs\confidence-070\catalogue.db
C:\PhotoIdentity\M16\runs\confidence-060\catalogue.db
C:\PhotoIdentity\M16\runs\confidence-050\catalogue.db
```

Never reuse or mutate the confidence-0.9 baseline catalogue. Do not overwrite a completed candidate catalogue. The source directory must contain the unchanged 100-photo set and nothing else.

The following Windows PowerShell command processes confidence `0.7`, which is the next governed candidate after the failed `0.8` review. Change only `$confidence` and `$confidenceTag` when advancing to a later governed candidate.

```powershell
$repo = "C:\Kod\codex\Photo Identity Indexer"
$sample = "C:\PhotoIdentity\M16\sample"
$confidence = 0.7
$confidenceTag = "070"
$candidateRoot = "C:\PhotoIdentity\M16\runs\confidence-$confidenceTag"
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
    --padding 0.25 2>&1 | Tee-Object -FilePath $candidateLog

if ($LASTEXITCODE -ne 0) {
    throw "Candidate processing failed for confidence $confidence."
}
```

Record the run ID printed by `batch start`. Check its durable status before opening the comparison application:

```powershell
$runId = "REPLACE_WITH_PRINTED_RUN_ID"

dotnet run `
    --project .\src\PhotoIdentity.Cli `
    -- `
    batch status `
    --database $candidateDb `
    --run $runId
```

All intended photos must complete successfully. Resume the same run after an interruption:

```powershell
dotnet run `
    --project .\src\PhotoIdentity.Cli `
    -- `
    batch resume `
    --database $candidateDb `
    --run $runId
```

The governed order is:

1. `0.8` with tag `080` — completed and failed
2. `0.7` with tag `070` — next
3. `0.6` with tag `060` — only if required
4. `0.5` with tag `050` — only if required

Stop the sweep as soon as a candidate meets the complete M16 gate, unless the milestone decision explicitly requires another governed run.

## Step 3: attach the completed candidate run

Start the application against the completed candidate catalogue while retaining the same private detector-evaluation root:

```powershell
$candidateDb = "C:\PhotoIdentity\M16\runs\confidence-070\catalogue.db"
$evaluationRoot = "C:\PhotoIdentity\M16\private\evaluation-sessions"

$env:PhotoIdentity__DatabasePath = $candidateDb
$env:PhotoIdentity__DetectorEvaluationRoot = $evaluationRoot

Set-Location -LiteralPath "C:\PhotoIdentity\M16\review-app"
dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open `http://localhost:5080/detector-comparisons`, select the frozen baseline, select the completed candidate processing run and create a comparison such as `M16 confidence 0.7`.

Comparison creation:

- requires the exact frozen photo set;
- verifies every full source SHA-256;
- snapshots candidate detections into the private comparison file;
- applies deterministic IoU matching; and
- surfaces only unmatched, duplicate or ambiguous components.

Clean one-to-one matches are counted automatically and do not appear in the manual queue.

Saved comparisons use comparison-scoped photo URLs. When the original candidate revision is not present in the currently opened catalogue, the application can resolve the same staged filename with the complete frozen SHA-256. This allows an existing comparison to remain readable after switching back to the baseline or another isolated catalogue, provided the verified source photo is locally available.

## Step 4: review one surfaced exception photo at a time

The comparison workspace keeps the complete image and decisions visible together on desktop. The decision panel scrolls independently, while the image stays in view. On narrow screens, the image is bounded and save actions remain reachable.

Use the overlay legend:

- `R1`, `R2`, and so on are reference faces from the frozen baseline.
- `C1`, `C2`, and so on are detections from the candidate run.

For each exception photo:

- match a candidate detection to one reference face when they represent the same face;
- classify an unmatched candidate as **False detection**;
- classify an additional detection of an already counted face as **Duplicate detection**;
- mark a reference face as missed when no candidate represents it; and
- add neutral notes only when they help explain the correction.

When a photo contains reference faces but no candidate review boxes, those reference faces are necessarily detector misses. The workspace counts them automatically and shows an informational completed row instead of redundant missed-face checkboxes. Use the normal save action to persist them.

Manual matches are one-to-one. Every surfaced candidate and reference decision must be resolved before **Save and next** becomes available.

Review controls:

- **Previous** and **Next** move between exception photos.
- **Save** persists the current corrections without leaving the photo.
- **Save and next** persists a complete photo and advances.
- **Fit** shows the complete image and is the default for every photo.
- 100%, 200%, 400%, zoom-step and drag-to-pan support detail inspection.
- Moving to another photo resets zoom, pan, decision-panel scroll and transient marker focus.
- Selecting or focusing a decision highlights the associated `R` or `C` marker, and selecting a marker reveals its decision.

Corrections are saved atomically under:

```text
<DetectorEvaluationRoot>\comparisons
```

The application can be restarted and the comparison resumed without repeating automatic matches or prior manual corrections.

## Step 5: assess and export the M16 gate

After all exception photos are resolved, record whether a material failure category remains incompatible with the intended archive workflow. The gate remains `pending` until:

- every exception decision is resolved; and
- the material-category assessment is recorded.

The comparison calculates:

- overall recall;
- recall for photos with five or more countable faces;
- source-group summaries;
- primary-category summaries;
- false and duplicate totals; and
- the four-part M16 gate.

The fixed M16 target is:

- overall recall at least `90%`;
- five-plus-face recall at least `85%`;
- no more than `10` false or duplicate detections; and
- no material failure category.

Use **Export summaries** to create a spreadsheet-compatible CSV. Keep detailed comparison files and exports private. Commit only privacy-safe aggregate evidence.

Record the candidate as passed or failed only after the complete gate is no longer pending. Do not infer which sub-gate failed in public documentation unless a privacy-safe aggregate decision explicitly records it.

## Step 6: proceed or stop

- If the candidate passes the complete M16 gate, stop the threshold sweep and continue to the governed rollout work.
- If it fails, preserve the candidate record and continue to the next governed threshold.
- Confidence `0.8` has failed, so the next candidate is `0.7`.
- When all governed threshold candidates fail, use the recorded private category evidence to decide whether WI-0036 multi-scale YuNet is required.

Do not copy candidate detections into the canonical reviewed catalogue during comparison. Any accepted detector change still requires WI-0038 rollout and provenance controls.
