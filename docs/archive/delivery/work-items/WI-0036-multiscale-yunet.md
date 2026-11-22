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

WI-0035 completed on 2026-08-06 after the immutable confidence-0.9 baseline and every governed threshold candidate from `0.8` through `0.5` failed the complete M16 gate. Confidence tuning alone was therefore insufficient.

The multi-scale implementation was delivered through pull request [#82](https://github.com/erikwasa/Photo-Identity-Indexer/pull/82).

## Delivered implementation

The implementation adds an opt-in `full-image-plus-tiles` detector pipeline while retaining `single-pass` as the default for existing runs.

The multi-scale path:

- runs one aspect-ratio-preserving full-image pass;
- plans deterministic overlapping source-pixel tiles in row-major order;
- letterboxes every pass into the pinned YuNet model input without stretching;
- maps boxes and five landmarks from pass coordinates into original-image normalised coordinates;
- merges full-image and tile detections with deterministic global non-maximum suppression; and
- stores pipeline name, confidence, tile size, overlap and merge threshold in the durable processing-run configuration.

The governed defaults were:

- pipeline: `full-image-plus-tiles`;
- tile size: `1024` source pixels;
- tile overlap: `0.20`;
- global merge IoU threshold: `0.30`; and
- per-pass YuNet NMS: unchanged at `0.30`.

## Governed evaluation

Both multi-scale candidates used new isolated catalogues and output directories. The source set, source hashes, YuNet model revision, embedder revision, padding, face-counting ground truth and comparison IoU remained unchanged.

### Confidence 0.9

The first candidate used confidence `0.9` to isolate the pipeline change from the earlier threshold sweep.

The maintainer completed the private comparison review on 2026-08-07. Multi-scale confidence `0.9` failed the complete M16 gate, although it performed better than the single-pass confidence-0.9 baseline and the single-pass confidence-0.8 candidate. Detailed counts and category evidence remain private.

This result established that deterministic tiling recovered useful faces but was not sufficient at the conservative threshold.

### Confidence 0.7 follow-up

A single combined threshold-plus-multi-scale follow-up at confidence `0.7` was explicitly selected before processing because single-pass confidence `0.7` and `0.6` had produced the strongest recall results in WI-0035.

The multi-scale confidence-0.7 run returned more than 100 false or duplicate detections across the fixed sample. That exceeds the M16 maximum of 10 by an order of magnitude and makes the complete gate impossible regardless of any recall improvement.

Confidence `0.6` was therefore not run. Lowering the threshold further would predictably increase rather than resolve the already disqualifying false/duplicate workload, and another run would not be a proportionate use of review effort.

## Final outcome

No governed YuNet multi-scale configuration meets the complete M16 target:

- confidence `0.9` improved recall relative to relevant earlier YuNet runs but still failed the complete gate;
- confidence `0.7` produced more than 100 false or duplicate detections; and
- confidence `0.6` was intentionally skipped under the predeclared stop rationale.

The implementation remains available as an opt-in, exactly provenance-recorded experimental pipeline. It is not approved for canonical rollout.

WI-0036 is complete as an evaluation work item. Continue to [WI-0037](WI-0037-detector-candidate.md) to qualify and evaluate a different face-detector family.

## Scope

- Preserve image aspect ratio for each pass.
- Run a full-image pass plus deterministic overlapping tiles.
- Map tile boxes and landmarks into original-image coordinates.
- Merge all passes with deterministic global non-maximum suppression.
- Persist threshold, scale, tile, overlap and merge policy as detector-pipeline provenance.
- Re-evaluate the exact WI-0034 sample and counting rules.

## Acceptance criteria

- [x] Small-face information is not lost through unconditional whole-image square resizing alone.
- [x] Cross-tile duplicates are merged deterministically.
- [x] Output geometry remains valid in original-image normalised coordinates.
- [x] Automated tests cover mapping, overlap, duplicate suppression and deterministic ordering.
- [x] The same 100-photo sample reports recall, false detections, runtime and review effort.

## Privacy

The repository records only privacy-safe workflow conclusions. Detailed counts other than the disqualifying aggregate `100+` false-or-duplicate result, filenames, paths, source images, face boxes, category values, databases and comparison files remain outside Git.

Follow [Multi-scale detector runs](../../operations/multiscale-detector-runs.md) for the retained procedure and final decision record.
