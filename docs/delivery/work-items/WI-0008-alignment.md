---
id: WI-0008
title: Implement face crops and alignment
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0007]
affected_modules: [PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Recognition.Tests]
---

# WI-0008: Implement face crops and alignment

## Objective

Create reusable padded review crops and deterministic five-point aligned model inputs with boundary handling.

## Acceptance criteria

- [x] Padded crops remain inside source bounds.
- [x] Alignment output has fixed dimensions and protocol ID.
- [x] Visual fixtures cover edge faces and rotated images.
- [x] Crop hashes are stable across repeated runs.

## Implemented surface

- `OpenCvFaceCropper` expands detected boxes by a configurable ratio, rounds outward, clamps to the decoded source bounds and returns packed application-owned pixels.
- `PaddedFaceCrop` records the exact source rectangle plus a canonical SHA-256 digest over crop protocol metadata and raw packed pixels.
- `OpenCvFaceAligner` implements the existing neutral `IFaceAligner` contract.
- The supported `sface-five-point-v1` protocol produces fixed 112×112 outputs using the OpenCV SFace reference template.
- Core anatomical left/right landmark semantics are mapped explicitly to the SFace image-space point ordering.
- The similarity transform is deterministic, rejects degenerate landmarks and uses constant boundary fill when an aligned sample reaches outside the source image.
- OpenCV `Mat` values remain internal to the adapter.

## Tests

Synthetic visual fixtures avoid committing private or biometric image data while exercising the same geometry paths:

- an edge-touching face verifies outward padding, source-bound clamping and row packing;
- equivalent packed and padded source strides produce the same crop bytes and digest;
- the canonical five-point template verifies protocol identity and fixed dimensions;
- a rotated synthetic face verifies that alignment restores the canonical model input;
- unknown protocols and degenerate landmarks fail explicitly.

## Commands

```powershell
dotnet test tests/PhotoIdentity.Recognition.Tests/PhotoIdentity.Recognition.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Verification

Draft pull request [#14](https://github.com/erikwasa/Photo-Identity-Indexer/pull/14) contains the implementation.

The SFace destination coordinates and 112×112 output dimensions follow OpenCV's `FaceRecognizerSF::alignCrop` implementation.

GitHub Actions run [30167129536](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30167129536) passed restore, Release build, all tests, living-document validation, generated-document checks and the Windows mixed-media verifier on the completed implementation.
