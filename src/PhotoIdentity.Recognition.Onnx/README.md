# PhotoIdentity.Recognition.Onnx

This adapter owns ONNX-model-specific concerns while exposing only `PhotoIdentity.Core` contracts to the rest of the application.

## Current responsibility

WI-0005 adds model governance and installation:

- strict JSON manifests under `models/manifests`
- immutable model identity based on weights and preprocessing metadata
- SHA-256 and file-size verification
- atomic installation after successful verification
- separate code, weights and training-data licence records

Actual ONNX inference is added by later work items.

## Invariants

- A model file is never considered installed until its size and SHA-256 match.
- Model binaries remain outside Git.
- Download URLs are HTTPS and pinned to an upstream repository revision.
- Unknown manifest fields are rejected rather than silently ignored.
- An embedding manifest must declare alignment, output dimensions and distance metric.
