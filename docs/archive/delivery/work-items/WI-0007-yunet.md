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

- [x] Representative faces receive visually correct boxes and landmarks.
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

Pull request [#12](https://github.com/erikwasa/Photo-Identity-Indexer/pull/12) introduced the implementation. Pull request [#13](https://github.com/erikwasa/Photo-Identity-Indexer/pull/13) corrected the pinned model input from the erroneous 320×320 manifest value to the ONNX file's fixed 640×640 shape.

Deterministic tests cover preprocessing channel order, normalised boxes, landmark ordering, confidence thresholding, the pinned 640×640 model input size and explicit invalid-shape failures without downloading model binaries.

GitHub Actions runs [30164251213](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30164251213) and [30166209245](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30166209245) passed the complete Windows workflow for the implementation and manifest correction.

The developer then ran the installed YuNet model against representative private photos and confirmed that boxes and five-point landmarks were visually correct. No private source photos or generated overlays were committed.
