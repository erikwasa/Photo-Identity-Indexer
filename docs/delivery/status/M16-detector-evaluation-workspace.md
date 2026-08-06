# M16 detector evaluation workspace status

Status date: 2026-08-05

Status update branch: `agent/WI-0040-status-and-threshold-080`

## Current outcome

The fixed private 100-photo set has been processed and fully reviewed with the pinned YuNet detector at confidence `0.9`. The detector-evaluation workspace retained complete photo context, zero-detection photos, per-detection classifications, missed-face geometry, source groups and primary categories without changing canonical identity review state.

The completed confidence-0.9 baseline was evaluated against the predeclared M16 decision target on 2026-08-05 and **did not pass**. The immutable authored session remains the reference ground truth for threshold experiments.

Confidence `0.8` was then processed in an isolated catalogue, compared with the frozen baseline and fully reviewed by the maintainer on 2026-08-05. It also **failed the complete M16 gate**. Detailed counts, filenames, paths, images and category values remain private. WI-0035 remains active, and confidence `0.7` is the next governed candidate.

WI-0040 is complete. PR #79 delivered the viewport-fitted comparison workspace, and PR #80 fixed comparison-photo retrieval across isolated catalogues. The maintainer tested the merged workflow on 2026-08-05 and confirmed that it works as expected.

## Delivered workspace

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

### Viewport-fitted review workspace — PR #79

- a stable viewport-relative review area;
- complete-image fitting by both available width and height without cropping;
- a desktop split layout with the image and decisions visible together;
- independent decision-panel scrolling while the image remains visible;
- continuously reachable previous, next, completion, save and save-and-next actions;
- Fit, 100%, 200%, 400%, zoom-step and drag-to-pan controls;
- reset of zoom, pan, decision-panel scroll and transient focus on every photo change;
- pointer and keyboard linkage between `R`/`C` overlays and decision controls;
- collapsed comparison metrics, summaries, instructions and qualitative-gate controls below the active workspace; and
- a bounded narrow-screen layout with sticky save actions.

### Cross-catalogue comparison-photo retrieval — PR #80

- comparison-scoped image URLs rather than raw candidate-revision URLs;
- direct candidate-revision lookup when the original revision exists in the active catalogue;
- fallback lookup by staged filename and complete frozen SHA-256 when another isolated catalogue is active;
- validation that the requested photo belongs to the saved comparison;
- retention of path-containment, file-size, reparse-point and media-type checks; and
- integration coverage for restarting a saved comparison against the baseline catalogue.

GitHub Actions build #495 passed the release build, complete test suite, living-document validation, generated-document verification, review-application smoke verification and Windows PowerShell mixed-media verification. Human verification then confirmed the merged WI-0040 workflow behaves as expected.

## Active next work

WI-0035 continues the fixed confidence sweep against the unchanged 100-photo set.

Completed governed candidates:

1. confidence `0.9` baseline — failed;
2. confidence `0.8` candidate — failed.

Next governed steps:

1. process the unchanged sample at confidence `0.7` into a new isolated catalogue and output directory;
2. attach the completed `0.7` processing run to the frozen confidence-0.9 ground truth;
3. resolve only surfaced exception photos in the viewport-fitted comparison workspace;
4. record the material-category assessment;
5. export and assess the complete M16 gate; and
6. stop if the gate passes, otherwise continue in order with `0.6` and `0.5` only as required.

The complete Windows commands and current comparison workflow are documented in [`docs/operations/detector-comparison-runs.md`](../../operations/detector-comparison-runs.md).

## Privacy

No private image names, source paths, face boxes, counts, ground-truth files, databases or detector outputs are committed. Repository evidence records only the fixed method, completed reviews, pass/fail decisions, delivered comparison capability and active governed work.
