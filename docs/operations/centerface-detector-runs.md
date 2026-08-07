# CenterFace detector runs

Use this procedure for the first governed WI-0037 CenterFace candidate. It separates functional/runtime smoke verification from the immutable 100-photo M16 comparison so the smoke set cannot become an informal threshold-tuning set.

## Fixed candidate identity

The first candidate is:

- detector model `centerface-2019-fp32`;
- SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`;
- execution runtime `opencv-dnn`;
- detector confidence `0.5`;
- detector pipeline `single-pass`;
- RGB float32 input with scale `1.0` and zero mean;
- source long edge bounded to `1600` pixels before dimensions are rounded up to multiples of `32`;
- deterministic CenterFace IoU `0.30` NMS;
- embedder `sface-2021dec-fp32`;
- padding `0.25`; and
- unchanged `sface-five-point-v1` alignment.

Do not change the confidence, maximum input edge, resize rule, NMS or landmark mapping during the first candidate run. A changed value is a separately approved candidate, not a retry of the same run.

## Runtime correction after the first smoke

The first five-image smoke run used ONNX Runtime and run ID `fbe99826-96ce-44af-b64f-3e6a3b8d93b1`. All five jobs failed before detector output because the pinned upstream ONNX artifact declares stale static input metadata equivalent to `10 x 3 x 32 x 32`. ONNX Runtime rejected the governed photo-dependent tensors.

That run is retained as runtime-compatibility evidence. It is **not** detector-quality evidence and does not justify changing confidence or preprocessing.

The pinned upstream CenterFace reference executes this same ONNX artifact through OpenCV DNN with dimensions rounded to multiples of `32`. The project therefore keeps the same ONNX bytes, preprocessing, decoder, confidence and smoke images while changing only the execution runtime to OpenCV DNN.

Do not reuse the failed ONNX Runtime database for the corrected smoke.

## 1. Validate the implementation on Windows

From the repository root:

```powershell
$repo = "C:\Kod\codex\Photo Identity Indexer"
Set-Location -LiteralPath $repo

dotnet restore .\PhotoIdentity.slnx
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

dotnet build .\PhotoIdentity.slnx --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

dotnet test .\PhotoIdentity.slnx --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

dotnet run --project .\tools\PhotoIdentity.Docs --configuration Release --no-build -- validate
if ($LASTEXITCODE -ne 0) { throw "Documentation validation failed." }

dotnet run --project .\tools\PhotoIdentity.Docs --configuration Release --no-build -- generate --check
if ($LASTEXITCODE -ne 0) { throw "Generated documentation is stale." }
```

Do not continue if the model-manifest, decoder, preprocessing or existing YuNet tests fail.

## 2. Install and re-verify the exact model

```powershell
Set-Location -LiteralPath $repo

.\models\install-models.ps1 -Id centerface-2019-fp32
.\models\inspect-centerface-candidate.ps1 -OutputPath .\models\installed\centerface-2019-fp32\centerface.onnx
```

The installed file must still report SHA-256:

`77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`

Do not manually rename or substitute another CenterFace ONNX graph.

## 3. Repeat the bounded functional smoke through OpenCV DNN

Use the same small disposable folder of roughly three to five representative images that are **not** part of the fixed M16 100-photo sample. The smoke test answers only whether the graph loads, inference completes, geometry is plausible and aligned crops are usable. Do not use it to tune confidence.

Use a **new** root so the failed ONNX Runtime evidence remains intact:

```powershell
$smokeSource = "C:\PhotoIdentity\M16\centerface-smoke-source"
$smokeRoot = "C:\PhotoIdentity\M16\runs\centerface-smoke-opencv-050"
$smokeDb = Join-Path $smokeRoot "catalogue.db"
$smokeOutput = Join-Path $smokeRoot "outputs"
$smokeLog = Join-Path $smokeRoot "batch-start.log"

New-Item -ItemType Directory -Force -Path $smokeRoot,$smokeOutput | Out-Null

if (Test-Path -LiteralPath $smokeDb) {
    throw "Smoke catalogue already exists: $smokeDb"
}

Set-Location -LiteralPath $repo

& dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    --no-build `
    -- `
    batch start `
    --database $smokeDb `
    --source $smokeSource `
    --output $smokeOutput `
    --detector-model centerface-2019-fp32 `
    --embedder-model sface-2021dec-fp32 `
    --confidence 0.5 `
    --padding 0.25 `
    --detector-pipeline single-pass 2>&1 | Tee-Object -FilePath $smokeLog

if ($LASTEXITCODE -ne 0) {
    throw "CenterFace OpenCV DNN smoke processing failed."
}
```

Record the printed run ID and verify status:

```powershell
$smokeRunId = "REPLACE_WITH_PRINTED_RUN_ID"

dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    --no-build `
    -- `
    batch status `
    --database $smokeDb `
    --run $smokeRunId
```

The smoke passes only when:

- all intended images finish without OpenCV DNN/model errors;
- the printed detector model is `centerface-2019-fp32`;
- the detector confidence is `0.5` and pipeline is `single-pass`;
- detected boxes are visually plausible on the disposable images;
- a few generated `aligned.png` crops show the eyes, nose and mouth in sensible SFace alignment rather than mirrored or grossly displaced; and
- runtime and memory behavior are practical enough to attempt the bounded 100-photo evaluation.

Generated aligned crops are under the smoke output's `runs/<run-id>/assets/.../faces/.../aligned.png` hierarchy. Smoke evidence remains local; commit only the privacy-safe pass/fail conclusion.

If landmark alignment looks mirrored or otherwise systematically wrong, stop. Correct and re-review the mapping before touching the private M16 sample. Do not compensate by changing the identity model or alignment template.

## 4. Authorise the first M16 candidate

Before the full run, record that:

- the current Windows build/tests and corrected OpenCV DNN smoke passed;
- the exact model hash remained unchanged;
- the maintainer accepts the documented repository/model-weight interpretation and unresolved WIDER FACE training-data limitation for this local evaluation; and
- no smoke observation caused a threshold or preprocessing change.

Only then process the fixed M16 sample.

## 5. Process the unchanged 100-photo sample

Use a new isolated database, output directory and log. Keep the private source set and frozen ground truth unchanged.

```powershell
$sample = "C:\PhotoIdentity\M16\sample"
$candidateName = "centerface-opencv-050"
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
    throw "The CenterFace candidate processing run failed."
}
```

Record the run ID and confirm every intended photo completed:

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

Resume that exact run after interruption rather than creating another candidate:

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

## 6. Compare against the frozen ground truth

Use the same detector-comparison workspace and private evaluation root used for WI-0035 and WI-0036:

```powershell
$evaluationRoot = "C:\PhotoIdentity\M16\private\evaluation-sessions"

$env:PhotoIdentity__DatabasePath = $candidateDb
$env:PhotoIdentity__DetectorEvaluationRoot = $evaluationRoot

Set-Location -LiteralPath "C:\PhotoIdentity\M16\review-app"
dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open `http://localhost:5080/detector-comparisons`, select the immutable `M16 confidence 0.9 baseline`, select the completed CenterFace run and create a comparison whose name records `centerface-2019-fp32`, `opencv-dnn` and confidence `0.5`.

Comparison creation must verify the exact source filename set and every source SHA-256 before review begins.

Resolve only surfaced unmatched, duplicate or ambiguous cases. Retain runtime and review-effort evidence with the gate summary.

The unchanged M16 gate is:

- overall recall at least `90%`;
- five-plus-face recall at least `85%`;
- no more than `10` false or duplicate detections; and
- no material failure category incompatible with the archive workflow.

## Stop rule

Review the complete confidence-`0.5` result before changing any parameter.

- If it passes the complete M16 gate, stop candidate search and continue to WI-0038.
- If it fails because of a clearly bounded confidence trade-off, predeclare one follow-up configuration before processing it.
- If it fails materially, is operationally impractical or exposes a governance blocker, record the gap and return to the WI-0037 candidate registry.

Do not run an unplanned confidence sweep.
