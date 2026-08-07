# CenterFace detector runs

Use this procedure for the governed WI-0037 CenterFace candidate. Functional/runtime smoke verification remains separate from the immutable 100-photo M16 comparison so smoke observations cannot become informal threshold tuning.

## Fixed candidate identity

The first candidate remains `centerface-2019-fp32` with SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`, OpenCV DNN, confidence `0.5`, `single-pass`, RGB float32 scale `1.0` zero mean, source long edge bounded to `1600` before multiple-of-32 rounding, IoU `0.30` NMS, SFace `sface-2021dec-fp32`, padding `0.25`, and unchanged `sface-five-point-v1` alignment.

Do not change confidence, maximum input edge, resize rule, NMS or landmark mapping during runtime debugging.

## Smoke history and stop rule

Three disposable five-image smoke runs are retained:

1. `fbe99826-96ce-44af-b64f-3e6a3b8d93b1` — ONNX Runtime rejected stale static input metadata.
2. `8a74c35e-e214-47e6-ad47-176bebc6d7e3` — OpenCV DNN executed the graph but the adapter failed copying N-D outputs.
3. `84f6f779-5a56-4e85-8d41-ee8569dce4d2` — all five jobs completed, but visual review failed: the reviewed images included 8 detections for an eight-person group, 593 for a two-person image, 633 for a one-person image, and one unusable detection for a four-person image.

The third run blocks the private 100-photo sample. An independent CenterFace adapter documents bad results when one OpenCV model is reused across calls; the current project correction therefore creates/disposes a fresh OpenCV `Net` per image without changing candidate parameters.

## Validate the branch

```powershell
$repo = "C:\Kod\codex\Photo Identity Indexer"
Set-Location -LiteralPath $repo

dotnet restore .\PhotoIdentity.slnx
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

dotnet build .\PhotoIdentity.slnx --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

dotnet test .\PhotoIdentity.slnx --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
```

## Repeat the five-image smoke

Use the same disposable images and a new root:

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

The repeat passes only if all five images complete, detection counts are plausible across every image rather than exploding after the first call, boxes cover faces, and several `aligned.png` crops have sensible non-corrupted geometry. Do not tune confidence from this smoke.

If the run still produces hundreds of detections or corrupted crops, stop and investigate preprocessing/output semantics. Only a stable functional and visual smoke can authorise the unchanged 100-photo M16 comparison.
