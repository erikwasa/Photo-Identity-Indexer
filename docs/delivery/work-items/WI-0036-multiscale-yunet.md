---
id: WI-0036
title: Add multi-scale YuNet detection
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0035]
affected_modules: [PhotoIdentity.Recognition.Onnx, PhotoIdentity.Worker, PhotoIdentity.Cli, PhotoIdentity.Integration.Tests]
---

# WI-0036: Add multi-scale YuNet detection

## Objective

Improve recall for small and distant faces by evaluating a governed full-image plus tiled YuNet pipeline.

## Activation

WI-0035 completed on 2026-08-06 after the immutable confidence-0.9 baseline and every governed threshold candidate from `0.8` through `0.5` failed the complete M16 gate. Confidence tuning alone is therefore insufficient.

WI-0036 is active on `agent/WI-0036-multiscale-yunet`.

## Current implementation

The first implementation increment adds an opt-in `full-image-plus-tiles` detector pipeline while retaining `single-pass` as the default for existing runs.

The multi-scale path:

- runs one aspect-ratio-preserving full-image pass;
- plans deterministic overlapping source-pixel tiles in row-major order;
- letterboxes every pass into the pinned YuNet model input without stretching;
- maps boxes and five landmarks from pass coordinates into original-image normalised coordinates;
- merges full-image and tile detections with deterministic global non-maximum suppression; and
- stores pipeline name, confidence, tile size, overlap and merge threshold in the durable processing-run configuration.

The initial implementation defaults are:

- pipeline: `full-image-plus-tiles`;
- tile size: `1024` source pixels;
- tile overlap: `0.20`;
- global merge IoU threshold: `0.30`; and
- per-pass YuNet NMS: unchanged at `0.30`.

These are governed experiment inputs, not an approved rollout configuration. The exact private 100-photo sample must still be processed and reviewed before the work item gate can be decided.

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

## Governed evaluation

Use a new isolated catalogue and output directory. Keep the source set, source hashes, YuNet model revision, embedder revision, padding, face-counting ground truth and comparison IoU unchanged.

The first multi-scale candidate should use confidence `0.9` so the pipeline change is isolated from the failed threshold sweep. Any later combined threshold-plus-multi-scale candidate requires a separately recorded decision before processing.

Follow [Multi-scale detector runs](../../operations/multiscale-detector-runs.md).

## Gate

When multi-scale YuNet passes, cancel WI-0037 and continue to WI-0038. Otherwise continue to WI-0037.
