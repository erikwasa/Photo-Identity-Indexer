---
id: WI-0006
title: Implement image decoding
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0003]
affected_modules: [PhotoIdentity.Imaging.OpenCv]
---

# WI-0006: Implement image decoding

## Objective

Implement the image abstraction for JPEG and PNG with orientation normalisation, colour conversion, resizing and explicit unsupported-format results.

## Acceptance criteria

- [ ] Rotated fixtures decode into the expected orientation.
- [ ] Core contracts expose no OpenCV matrices.
- [ ] Cancellation and corrupt-media errors are handled.
- [ ] HEIC remains a replaceable future adapter.
