# CenterFace detector runs

Use this procedure for the governed WI-0037 CenterFace candidate. Functional/runtime smoke verification remains separate from the immutable 100-photo M16 comparison so smoke observations cannot become informal threshold tuning.

## Fixed candidate identity

The first candidate remains:

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

Do not change confidence, maximum input edge, resize rule, NMS or landmark mapping during runtime debugging. A changed value is a separately approved candidate.

## Smoke history and current stop rule

Three disposable five-image smoke runs are retained as evidence:

1. `fbe99826-96ce-44af-b64f-3e6a3b8d93b1` — ONNX Runtime rejected the model's stale static input metadata.
2. `8a74c35e-e214-47e6-ad47-176bebc6d7e3` — OpenCV DNN executed the graph but the adapter failed while copying four-dimensional output tensors.
3. `84f6f779-5a56-4e85-8d41-ee8569dce4d2` — all five jobs completed after N-D marshalling was fixed, but visual review failed badly: one eight-person photo yielded eight faces, while other reviewed images yielded `593`, `633`, or one unusable detection/crop.

The third run is not detector-quality evidence and does not authorise the private 100-photo sample.

An independent CenterFace adapter documents problematic results when one OpenCV model instance is reused across calls. The current project correction therefore creates and disposes a fresh OpenCV `Net` for each image inference while keeping all candidate parameters unchanged.

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

Do not continue if the model-manifest, preprocessing, decoder or existing YuNet tests fail.

## 2. Re-verify the exact model

```powershell
Set-Location -LiteralPath $repo

.\models\install-models.ps1 -Id centerface-2019-fp32
.\models\inspect-centerface-candidate.ps1 -OutputPath .\models\installed\centerface-2019-fp32\centerface.onnx
```

The installed file must still report SHA-256:

`77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`

Do not substitute another CenterFace ONNX graph.

## 3. Repeat the bounded smoke with per-image OpenCV network state

Use the same five disposable images and a **new** root. Keep every earlier smoke root intact.

```powershell
$smokeSource = "C:\PhotoIdentity\M16\centerface-smoke-source"
$smokeRoot = "C:\PhotoIdentity\M16\runs\centerface-smoke-opencv-freshnet-050"
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
    throw "CenterFace per-image-network smoke processing failed."
}
```

Record the run ID and inspect the result:

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

The repeat smoke passes only when:

- all five images complete without runtime errors;
- detection counts are plausible for every disposable image rather than exploding after the first call;
- the known eight-person group remains approximately correct rather than merely being the only valid first image;
- boxes visually cover faces rather than arbitrary background regions;
- several `aligned.png` crops show sensible, non-corrupted eye/nose/mouth geometry; and
- no systematic upside-down, mirrored or indecipherable crops remain.

Generated aligned crops are under `outputs/runs/<run-id>/assets/.../faces/.../aligned.png`.

Do not tune confidence from these images. If the repeated run still produces hundreds of detections or corrupted crops, stop and investigate preprocessing/output semantics.

## 4. Authorise the first M16 candidate only after smoke passes

Before the full run, record that:

- the current Windows build/tests pass;
- the per-image-network smoke passes both functional and visual checks;
- the exact model hash remains unchanged;
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

New-Item -ItemType Directory -Force -Path $candidateRoot,$candidateOutput | Out-Null

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

Record the run ID and confirm every intended photo completed before comparison.

## 6. Compare against the frozen ground truth

Use the same detector-comparison workspace and private evaluation root used for WI-0035 and WI-0036. The unchanged M16 gate is:

- overall recall at least `90%`;
- five-plus-face recall at least `85%`;
- no more than `10` false or duplicate detections; and
- no material failure category incompatible with the archive workflow.

## Stop rule

Review the complete confidence-`0.5` result before changing any parameter. Do not run an unplanned confidence sweep.
