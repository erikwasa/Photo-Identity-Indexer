---
id: WI-0008
title: Implement face crops and alignment
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0007]
affected_modules: [PhotoIdentity.Imaging.OpenCv]
---

# WI-0008: Implement face crops and alignment

## Objective

Create reusable padded review crops and deterministic five-point aligned model inputs with boundary handling.

## Acceptance criteria

- [ ] Padded crops remain inside source bounds.
- [ ] Alignment output has fixed dimensions and protocol ID.
- [ ] Visual fixtures cover edge faces and rotated images.
- [ ] Crop hashes are stable across repeated runs.
