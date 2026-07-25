---
id: M01
title: Single-image inference
status_source: ../status/milestones.yaml
depends_on: [M00]
---

# M01: Single-image inference

## Outcome

`photoid inspect <image-path>` detects faces in one JPEG or PNG, saves annotated output and crops, aligns faces, creates SFace embeddings and records timings and model manifests.

## Work items

WI-0005 through WI-0010, plus WI-0026 for the local Windows verification checkpoint before ONNX inference.

## Exit criteria

- Face boxes and orientation are visually correct.
- Embeddings are reproducible within tolerance.
- CPU inference works on Windows.
- Failures are actionable.
- The local verification harness has passed with real private images before YuNet implementation begins.

## Completion

All M01 work items are complete. Pull request #16 merged the end-to-end inspection command, its final Windows workflow passed, and the developer verified the full YuNet and SFace path with representative private images on 2026-07-25.
