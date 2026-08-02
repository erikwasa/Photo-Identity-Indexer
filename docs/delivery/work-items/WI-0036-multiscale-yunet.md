---
id: WI-0036
title: Add multi-scale YuNet detection
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0035]
affected_modules: [PhotoIdentity.Recognition.Onnx, PhotoIdentity.Worker, PhotoIdentity.Integration.Tests]
---

# WI-0036: Add multi-scale YuNet detection

## Objective

Improve recall for small and distant faces by evaluating a governed full-image plus tiled YuNet pipeline.

## Scope

- Preserve image aspect ratio for each pass.
- Run a full-image pass plus deterministic overlapping tiles.
- Map tile boxes and landmarks into original-image coordinates.
- Merge all passes with deterministic global non-maximum suppression.
- Persist threshold, scale, tile, overlap and merge policy as detector-pipeline provenance.
- Re-evaluate the exact WI-0034 sample and counting rules.

## Acceptance criteria

- [ ] Small-face information is not lost through unconditional whole-image square resizing alone.
- [ ] Cross-tile duplicates are merged deterministically.
- [ ] Output geometry remains valid in original-image normalised coordinates.
- [ ] Automated tests cover mapping, overlap, duplicate suppression and deterministic ordering.
- [ ] The same 100-photo sample reports recall, false detections, runtime and review effort.

## Gate

When multi-scale YuNet passes, cancel WI-0037 and continue to WI-0038. Otherwise continue to WI-0037.
