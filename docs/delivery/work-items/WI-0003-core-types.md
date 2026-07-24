---
id: WI-0003
title: Define core identifiers and contracts
milestone: M00
status_source: ../status/work-items.yaml
depends_on: [WI-0002]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Core.Tests]
---

# WI-0003: Define core identifiers and contracts

## Objective

Create application-owned identifiers, geometry, landmarks, embeddings, model descriptors and recognition/source contracts without infrastructure types.

## Acceptance criteria

- [ ] Strong IDs cannot be interchanged accidentally.
- [ ] Geometry validates dimensions and coordinate spaces.
- [ ] IoU and vector behaviour are unit-tested.
- [ ] Core has no EF Core, OpenCV, ONNX Runtime, Azure SDK or Graph dependency.
