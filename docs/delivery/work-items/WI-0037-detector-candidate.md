---
id: WI-0037
title: Evaluate another face detector
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0036]
affected_modules: [Models, PhotoIdentity.Recognition.Onnx, Evaluation]
---

# WI-0037: Evaluate another face detector

## Objective

Evaluate a governed detector candidate only when the fixed and multi-scale YuNet options remain below the M16 target.

## Scope

- Select a candidate with an acceptable licence, provenance and local runtime path.
- Pin exact model identity, hash, preprocessing and output semantics.
- Adapt landmarks or alignment inputs without weakening the SFace contract.
- Compare the candidate on the exact WI-0034 sample.
- Include recall by category, false detections, runtime and review effort.

## Acceptance criteria

- [ ] Candidate licensing and training-data limitations are documented.
- [ ] Exact model and pipeline provenance is immutable.
- [ ] The comparison uses unchanged source photos and ground truth.
- [ ] No score automatically becomes a canonical person label.
- [ ] A human-reviewed recommendation identifies the first acceptable pipeline.

## Gate

The selected candidate continues to WI-0038. If no candidate meets the target, record the remaining gap before changing the product target or expanding the search.
