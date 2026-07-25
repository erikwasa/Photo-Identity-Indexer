# Build context

## Current milestone

**M01 — Single-image inference**

## Current work item

**WI-0007 — Implement YuNet detection**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0007-yunet`
- Draft pull request: [#12 — Implement YuNet face detection](https://github.com/erikwasa/Photo-Identity-Indexer/pull/12)

## Objective

Add the pinned YuNet ONNX face detector behind the neutral `IFaceDetector` contract with deterministic preprocessing, output parsing, landmarks, confidence filtering and timing metadata.

## Relevant files

- `src/PhotoIdentity.Recognition.Onnx/YuNet/YuNetFaceDetector.cs`
- `src/PhotoIdentity.Recognition.Onnx/YuNet/YuNetOutputParser.cs`
- `src/PhotoIdentity.Recognition.Onnx/YuNet/OnnxYuNetInferenceSession.cs`
- `src/PhotoIdentity.Recognition.Onnx/PhotoIdentity.Recognition.Onnx.csproj`
- `tests/PhotoIdentity.Recognition.Tests/YuNetDetectorTests.cs`
- `models/manifests/yunet-2023mar-fp32.json`
- `docs/delivery/work-items/WI-0007-yunet.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
./models/install-models.ps1 -Id yunet-2023mar-fp32
dotnet test tests/PhotoIdentity.Recognition.Tests/PhotoIdentity.Recognition.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test

- Input image frames are resized and converted to channel-first float32 tensors using manifest preprocessing metadata.
- All twelve YuNet tensors are required and their shapes are checked against the model input dimensions and strides.
- Class and object scores, boxes and five landmarks are decoded with the OpenCV YuNet semantics.
- Confidence thresholding, top-K limiting and non-maximum suppression are deterministic.
- Output boxes and landmarks use application-owned normalised geometry.
- YuNet right/left landmark order is mapped to the core semantic contract.
- Model descriptor and preprocessing, inference and postprocessing durations are returned.
- Invalid shapes and non-finite model outputs fail explicitly.
- Representative private photos still require visual box and landmark inspection before completion.

## Verification

WI-0026 was completed after PR #11 merged, GitHub Actions run `30162808500` passed, and the developer confirmed the private JPEG, PNG, EXIF-rotated Pixel and unsupported-media checks locally.

PR #12 includes deterministic YuNet tests that do not download model binaries. GitHub Actions must pass on the final branch head before the pull request is ready for review.

## Known issues

- The current execution provider is CPU-only.
- The pinned 2023mar YuNet model has a fixed 320x320 input, so preprocessing resizes the whole image to that shape and maps results back through normalised coordinates.
- The repository has no public photo fixture suitable for visual correctness assertions; final verification uses private local images and must not commit outputs.

## Next action

Resolve any CI findings on draft pull request #12, run the installed YuNet model against representative private photos, inspect boxes and landmarks, then mark WI-0007 in review.
