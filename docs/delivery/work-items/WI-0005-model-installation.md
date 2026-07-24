---
id: WI-0005
title: Add model installation and verification
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0003]
affected_modules: [PhotoIdentity.Recognition.Onnx, models]
---

# WI-0005: Add model installation and verification

## Objective

Define model manifests and provide installation scripts that download YuNet and SFace, verify SHA-256 values and record licences.

## Acceptance criteria

- [ ] Model files remain ignored by Git.
- [ ] A mismatched hash prevents use.
- [ ] Manifests describe preprocessing, dimensions, alignment and output semantics.
- [ ] Code and weight licences are recorded separately.
