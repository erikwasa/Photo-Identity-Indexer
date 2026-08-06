# M16 detector evaluation workspace status

Status date: 2026-08-06

Active implementation branch: `agent/WI-0036-multiscale-yunet`

## Current outcome

The fixed private 100-photo set has been processed and fully reviewed with the pinned YuNet detector at confidence `0.9`. That immutable baseline failed the predeclared M16 decision target and remains the reusable face-level ground truth.

The maintainer subsequently completed isolated candidate runs at confidence `0.8`, `0.7`, `0.6` and `0.5` against the unchanged ground truth. Every candidate was fully reviewed and every complete M16 gate failed. Detailed counts, filenames, paths, images and category values remain private.

WI-0035 is complete. Threshold tuning alone is insufficient, and no confidence setting is approved for rollout.

WI-0036 is active. Its first implementation increment introduces an opt-in full-image plus tiled YuNet pipeline while retaining the existing single-pass path as the default for compatibility.

## Delivered evaluation workspace

### Baseline authoring and export — PRs #70, #71 and #72

- photo-level processing-run queries and no-cache API endpoints;
- original-photo streaming without source paths;
- stable ordering including zero-detection photos;
- detector overlays on the complete source photo;
- private spreadsheet CSV import and immutable-run validation;
- resumable private JSON sessions outside the catalogue;
- correct, background/unknown, false and duplicate classifications;
- direct missed-face geometry authoring;
- per-photo completion arithmetic;
- spreadsheet-compatible export; and
- Fit, 100%, 200% and 400% source-pixel zoom with panning and stable normalized geometry.

### Repeated-run comparison — PR #74

- reusable face-level ground truth frozen from the completed baseline;
- exact candidate-source validation by filename and full SHA-256;
- deterministic intersection-over-union connected-component matching;
- automatic handling of clean one-to-one matches;
- exception review for unmatched, duplicate and ambiguous components;
- resumable private correction and gate-assessment storage;
- overall, five-plus-face, source-group, category and M16 gate summaries; and
- spreadsheet-compatible summary export.

### Comparison-review clarity — PRs #76 and #77

- one exception photo at a time with previous, next and save-and-next actions;
- operator-facing candidate/reference terminology and decision-completion status;
- distinct pass, fail, pending, resolved and needs-review treatment;
- compact numbered `R` and `C` markers for clustered faces;
- removal of internal component IDs from the operator workflow; and
- automatic detector-miss classification when a photo has reference faces but no candidate review boxes.

### Viewport-fitted and cross-catalogue review — PRs #79 and #80

- complete-image fitting by both available width and height without cropping;
- a desktop split layout with the image and decisions visible together;
- independent decision-panel scrolling while the image remains visible;
- reachable navigation and save actions;
- zoom, pan and marker-to-decision linkage;
- reset of transient view state between photos;
- bounded narrow-screen behavior; and
- comparison-scoped image resolution across isolated catalogues using staged filename and full SHA-256 validation.

## Active WI-0036 implementation

The multi-scale implementation provides:

- one aspect-ratio-preserving full-image pass;
- deterministic row-major overlapping source-pixel tiles;
- letterboxing into the pinned YuNet input instead of stretching each pass;
- mapping of boxes and all five landmarks into original-image normalised coordinates;
- deterministic global non-maximum suppression across full-image and tile detections;
- a compatibility-preserving `single-pass` default;
- an explicit `full-image-plus-tiles` batch option; and
- durable confidence, pipeline, tile-size, overlap and merge-threshold provenance.

Automated tests cover tile coverage, aspect preservation, mapping, cross-pass duplicate suppression, deterministic ordering, CLI parsing and legacy run-configuration compatibility.

## Active next work

After implementation validation and merge:

1. process the unchanged private 100-photo set into a new isolated multi-scale catalogue;
2. use confidence `0.9`, tile size `1024`, overlap `0.20` and global merge IoU `0.30` for the first governed candidate;
3. attach the completed run to the frozen confidence-0.9 face-level ground truth;
4. resolve only surfaced exception photos;
5. retain runtime and review-effort evidence in addition to the existing gate metrics;
6. record the material-category assessment and export the complete M16 gate; and
7. continue to WI-0038 if the candidate passes, otherwise continue to WI-0037.

The Windows procedure is documented in [`docs/operations/multiscale-detector-runs.md`](../../operations/multiscale-detector-runs.md).

## Privacy

No private image names, source paths, face boxes, counts, ground-truth files, databases or detector outputs are committed. Repository evidence records only the fixed method, completed reviews, aggregate pass/fail decisions, implementation capability and active governed work.
