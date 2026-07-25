---
id: WI-0007
title: Implement YuNet detection
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0005, WI-0006, WI-0026]
affected_modules: [PhotoIdentity.Recognition.Onnx, PhotoIdentity.Recognition.Tests]
---

# WI-0007: Implement YuNet Detection

## Objective

Add a YuNet ONNX detector adapter with preprocessing, output parsing, landmarks, confidence thresholding and deterministic tests.

## Acceptance criteria

- [ ] Representative faces receive visually correct boxes and landmarks.
- [x] Output coordinates use the documented normalised image space.
- [x] Model descriptor and timing are recorded.
- [x] Invalid output shapes fail clearly.
- [x] WI-0026 has confirmed the local Windows runtime, models and real-photo decoding before ONNX inference begins.

## Implemented surface

- Microsoft ONNX Runtime CPU inference through disposable `OrtValue` buffers.
- Fixed-size 640×640 channel-first float32 preprocessing for the pinned `face_detection_yunet_2023mar.onnx` model.
- Strict validation of the twelve YuNet output names and shapes.
- OpenCV-compatible class/object score fusion and stride decoding.
- Bounding boxes plus five landmarks converted into normalised application geometry.
- YuNet right/left landmark ordering mapped explicitly to the core semantic contract.
- Deterministic confidence filtering, top-K selection and non-maximum suppression.
- Detection results include the model descriptor and preprocessing, inference and postprocessing durations.

## Commands

```powershell
dotnet test tests/PhotoIdentity.Recognition.Tests/PhotoIdentity.Recognition.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Verification

Pull request [#12](https://github.com/erikwasa/Photo-Identity-Indexer/pull/12) introduced the implementation.

Deterministic tests cover preprocessing channel order, normalised boxes, landmark ordering, confidence thresholding, the pinned 640×640 model input size and explicit invalid-shape failures without downloading model binaries.

GitHub Actions run [30164144590](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30164144590) passed restore, build, all tests, living-document validation, generated-document checks and the Windows mixed-media verifier smoke path.

The first real-model local run exposed that the manifest incorrectly declared 320×320 while the pinned ONNX file requires 640×640. The manifest and regression test were corrected before visual verification continued.

The remaining completion check is a successful local run with the installed YuNet model and representative private photos so boxes and landmarks can be inspected visually.
