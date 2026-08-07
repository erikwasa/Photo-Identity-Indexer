# CenterFace detector runs

Use this procedure for the governed WI-0037 CenterFace candidate. Functional/runtime smoke verification remains separate from the immutable 100-photo M16 comparison so smoke observations cannot become informal threshold tuning.

## Fixed candidate identity

The first candidate remains `centerface-2019-fp32` with SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`, OpenCV DNN, confidence `0.5`, `single-pass`, RGB float32 scale `1.0` zero mean, source long edge bounded to `1600` before multiple-of-32 rounding, IoU `0.30` NMS, SFace `sface-2021dec-fp32`, padding `0.25`, and unchanged `sface-five-point-v1` alignment.

Do not change confidence, maximum input edge, resize rule, NMS or landmark mapping before reviewing the complete 100-photo candidate.

## Smoke history

Four smoke stages are retained:

1. `fbe99826-96ce-44af-b64f-3e6a3b8d93b1` — ONNX Runtime rejected stale static input metadata.
2. `8a74c35e-e214-47e6-ad47-176bebc6d7e3` — OpenCV DNN executed the graph but the adapter failed copying N-D outputs.
3. `84f6f779-5a56-4e85-8d41-ee8569dce4d2` — all five jobs completed, but visual review exposed cross-image corruption when one OpenCV `Net` was reused.
4. The same disposable five-image set was repeated after PR #90 changed CenterFace to create/dispose a fresh OpenCV `Net` for every image. The maintainer reported that face outputs then matched the source images consistently on every image. The repeat run ID was not supplied to the repository record and is therefore not invented here.

The repeat clears the runtime-stability smoke gate. It does not select or tune a threshold.

## Governance gate before private processing

Before the fixed 100-photo M16 comparison:

- spot-check several `aligned.png` crops if they were not already included in the successful repeat review; and
- explicitly accept the documented CenterFace weight/training-data uncertainty for this local evaluation.

Production promotion or redistribution remains a separate governance decision even if the detector passes M16.

## Validate current main

```powershell
$repo = "C:\Kod\codex\Photo Identity Indexer"
Set-Location -LiteralPath $repo

git switch main
git pull --ff-only

dotnet restore .\PhotoIdentity.slnx
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

dotnet build .\PhotoIdentity.slnx --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

dotnet test .\PhotoIdentity.slnx --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

.\models\install-models.ps1 -Id centerface-2019-fp32
```

## Process the fixed 100-photo candidate

Use a fresh isolated candidate root. Do not reuse a YuNet or smoke database.

```powershell
$repo = "C:\Kod\codex\Photo Identity Indexer"
$sample = "C:\PhotoIdentity\M16\sample"
$candidateName = "centerface-050"
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
    --configuration Release `
    --no-build `
    -- `
    batch start `
    --database $candidateDb `
    --source $sample `
    --output $candidateOutput `
    --detector-model centerface-2019-fp32 `
    --embedder-model sface-2021dec-fp32 `
    --confidence 0.5 `
    --padding 0.25 `
    --detector-pipeline single-pass 2>&1 | Tee-Object -FilePath $candidateLog

if ($LASTEXITCODE -ne 0) {
    throw "The CenterFace confidence-0.5 candidate processing run failed."
}
```

Record the printed run ID. Confirm all 100 intended photos completed:

```powershell
$runId = "REPLACE_WITH_PRINTED_RUN_ID"

dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    --no-build `
    -- `
    batch status `
    --database $candidateDb `
    --run $runId
```

If processing is interrupted, resume the same durable run rather than creating another candidate:

```powershell
dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    --no-build `
    -- `
    batch resume `
    --database $candidateDb `
    --run $runId
```

## Comparison and review

Use the completed CenterFace catalogue with the existing private detector-evaluation root:

```powershell
$evaluationRoot = "C:\PhotoIdentity\M16\private\evaluation-sessions"

$env:PhotoIdentity__DatabasePath = $candidateDb
$env:PhotoIdentity__DetectorEvaluationRoot = $evaluationRoot

Set-Location -LiteralPath "C:\PhotoIdentity\M16\review-app"
dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open `http://localhost:5080/detector-comparisons`, select the immutable `M16 confidence 0.9 baseline`, select the completed CenterFace run, and create a comparison whose name clearly identifies `centerface-050`.

Comparison creation must verify the exact source filename set and every full source SHA-256 before review begins.

Resolve all surfaced unmatched, duplicate or ambiguous cases. Record the material-category assessment, then export the summaries.

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

## Decision after complete review

Do not tune CenterFace confidence before the full confidence-`0.5` comparison is reviewed.

If `centerface-050` meets the complete M16 gate, record the recommendation and continue to WI-0038 while keeping production licence/redistribution approval separate.

If it fails, preserve the complete comparison first. Then decide whether the failure pattern justifies one predeclared CenterFace follow-up configuration or whether WI-0037 should move to the next governed detector candidate. Do not choose that follow-up from the smoke images.
