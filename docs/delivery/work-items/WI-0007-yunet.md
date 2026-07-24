---
id: WI-0007
title: Implement YuNet detection
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0005, WI-0006]
affected_modules: [PhotoIdentity.Recognition.Onnx]
---

# WI-0007: Implement YuNet detection

## Objective

Add a YuNet ONNX detector adapter with preprocessing, output parsing, landmarks, confidence thresholding and deterministic tests.

## Acceptance criteria

- [ ] Representative faces receive visually correct boxes and landmarks.
- [ ] Output coordinates use the documented normalised image space.
- [ ] Model descriptor and timing are recorded.
- [ ] Invalid output shapes fail clearly.
