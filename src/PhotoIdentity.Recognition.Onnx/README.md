# PhotoIdentity.Recognition.Onnx

This adapter owns ONNX-model-specific concerns while exposing only `PhotoIdentity.Core` contracts to the rest of the application.

## Current responsibility

Model governance and installation:

- strict JSON manifests under `models/manifests`
- immutable model identity based on weights and preprocessing metadata
- SHA-256 and file-size verification
- atomic installation after successful verification
- separate code, weights and training-data licence records

YuNet face detection:

- deterministic resize and channel-first float32 preprocessing
- ONNX Runtime inference through disposable `OrtValue` buffers
- strict validation of the twelve YuNet output tensors
- OpenCV-compatible stride decoding, score fusion and non-maximum suppression
- conversion to application-owned normalised boxes and five-point landmarks
- model descriptor plus preprocessing, inference and postprocessing timing

## Invariants

- A model file is never considered installed until its size and SHA-256 match.
- Model binaries remain outside Git.
- Download URLs are HTTPS and pinned to an upstream repository revision.
- Unknown manifest fields are rejected rather than silently ignored.
- An embedding manifest must declare alignment, output dimensions and distance metric.
- ONNX Runtime, tensor names and tensor shapes do not cross the `IFaceDetector` boundary.
- Invalid or non-finite model outputs fail explicitly rather than producing partial detections.
