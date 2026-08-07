# M16 detector evaluation workspace status

Status date: 2026-08-07

Active implementation branch: `agent/WI-0037-centerface-adapter`

## Current outcome

The fixed private 100-photo set has been processed and fully reviewed with the pinned YuNet detector at confidence `0.9`. That immutable baseline failed the predeclared M16 decision target and remains the reusable face-level ground truth.

The maintainer subsequently completed isolated single-pass candidate runs at confidence `0.8`, `0.7`, `0.6` and `0.5` against the unchanged ground truth. Every candidate was fully reviewed and every complete M16 gate failed. Detailed counts, filenames, paths, images and category values remain private.

WI-0035 is complete. Threshold tuning alone is insufficient, and no single-pass confidence setting is approved for rollout.

WI-0036 delivered the opt-in full-image plus tiled YuNet pipeline through PR #82 and is now complete as an evaluation work item.

The maintainer completed two governed multi-scale runs on 2026-08-07:

- confidence `0.9` failed the complete gate but performed better than the single-pass confidence-0.9 baseline and single-pass confidence `0.8`; and
- confidence `0.7` returned more than 100 false or duplicate detections, making the maximum of 10 impossible.

A confidence-0.6 multi-scale run was intentionally skipped because lowering the threshold could not plausibly resolve the already disqualifying false/duplicate workload. No YuNet multi-scale configuration is approved for rollout.

WI-0037 is active. CenterFace ONNX is the first alternative detector. The exact artifact is now verified and pinned, and the dedicated adapter is implemented on the active branch. Full private-sample processing remains blocked on Windows build/runtime and human smoke verification.

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

## Completed WI-0036 implementation

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

The implementation remains available for reproducibility, but the private evaluation did not identify a rollout configuration.

## Active WI-0037 CenterFace implementation

The first target is the exact upstream `centerface.onnx` file committed at `Star-Clouds/CenterFace@b82ec0c4844e89fd5a0305986aed9bdf33c72585`.

The maintainer verified the artifact locally on Windows on 2026-08-07:

- byte size `7,532,772`;
- Git blob SHA-1 `1487d5fe214feb569865b225216b24c8f4ef1050`; and
- SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`.

The active branch now provides:

- immutable `centerface-2019-fp32` model identity and local installation path;
- an explicit bounded dynamic input-shape contract rather than falsely treating the graph as fixed `640x640`;
- pre-round source long edge `1600`, dimensions rounded up to multiples of `32`;
- RGB float32, scale `1.0`, zero-mean direct resize preprocessing;
- a dedicated ONNX Runtime session requiring outputs `537`, `538`, `539` and `540`;
- deterministic stride-four heatmap, scale, offset and five-landmark decoding;
- deterministic IoU `0.30` NMS;
- explicit right-eye, left-eye, nose, right-mouth and left-mouth mapping into the unchanged SFace alignment contract;
- local-batch detector selection by exact model ID with CenterFace restricted to `single-pass`; and
- synthetic tests for provenance, preprocessing, decoder geometry, thresholding, NMS, landmark mapping and malformed tensor shapes.

The repository root MIT licence is recorded separately from the provisional model-weight interpretation. WIDER FACE training-data rights remain not asserted by this project.

## Active next work

Before the full private comparison:

1. run the repository restore/build/test and living-document checks on Windows;
2. install `centerface-2019-fp32` through the normal exact-size/hash verifier;
3. process only a disposable smoke set that excludes the fixed M16 sample;
4. confirm real ONNX Runtime dynamic-shape inference and expected float32 output shapes;
5. visually confirm plausible detector boxes and non-mirrored SFace aligned crops; and
6. record acceptance of the documented licence/training-data uncertainty for local evaluation.

The smoke procedure is documented in [`docs/operations/centerface-detector-runs.md`](../../operations/centerface-detector-runs.md).

If the smoke gate passes, the first full private candidate is predeclared as confidence `0.5`, `single-pass`, maximum source long edge `1600` before multiple-of-32 rounding, SFace FP32 and padding `0.25`. Review the complete result before changing any parameter.

SCRFD remains technically attractive but is not the first target because InsightFace states an explicit non-commercial restriction for its supplied pretrained models. CenterFace production promotion or redistribution also remains blocked until its model-weight and training-data considerations are acceptable under the project's governance rules.

The candidate screen is retained in [`docs/models/face-detector-candidate-registry.md`](../../models/face-detector-candidate-registry.md).

## Privacy

No private image names, source paths, face boxes, detailed counts, ground-truth files, databases or detector outputs are committed. Repository evidence records only the fixed method, completed reviews, the aggregate `100+` false-or-duplicate conclusion, implementation capability and active governed work.
