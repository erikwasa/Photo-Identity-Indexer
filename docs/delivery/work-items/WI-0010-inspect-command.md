---
id: WI-0010
title: Build photoid inspect command
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0009]
affected_modules: [PhotoIdentity.Cli]
---

# WI-0010: Build `photoid inspect`

## Objective

Compose decoding, detection, cropping, alignment and embedding for one image and write annotated output, crops, embeddings, manifest and timings.

## Acceptance criteria

- [ ] Command works on Windows CPU for JPEG and PNG.
- [ ] Output is sufficient for visual inspection and reproducibility.
- [ ] Failures return useful exit codes and messages.
- [ ] No original image is modified.
