# M16 detector evaluation workspace status

Status date: 2026-08-05

Status update branch: `agent/WI-0040-viewport-review-workspace`

## Current outcome

The fixed private 100-photo set has been processed and fully reviewed with the pinned YuNet detector at confidence `0.9`. The detector-evaluation workspace retained the complete photo context, zero-detection photos, per-detection classifications, missed-face geometry, source groups and primary categories without changing canonical identity review state.

The completed confidence-0.9 baseline was evaluated against the predeclared M16 decision target on 2026-08-05 and **did not pass**. Detailed counts, filenames, paths, images and category values remain private. The immutable authored session is the reference ground truth for the threshold experiments.

PR #74 merged the repeated-run comparison slice into `main` on 2026-08-05. PR #76 then changed exception review to one photo at a time with plain-language decisions and clearer status treatment. PR #77 added compact `R`/`C` markers and automatically classified candidate-free reference faces as detector misses. WI-0039 remains complete and WI-0035 remains active.

Operator use of the refined comparison page identified one remaining workflow problem: source images still determine page height. Portrait and unusually large photos can force repeated browser-page scrolling between box markers and decision fields. WI-0040 implementation started on 2026-08-05 in branch `agent/WI-0040-viewport-review-workspace` and draft PR #79.

## Delivered workspace

### Slice 1 — merged in PR #70

- photo-level processing-run queries and no-cache API endpoints;
- original-photo streaming without source paths;
- stable ordering including zero-detection photos; and
- detector overlays on the complete source photo.

### Slice 2 — merged in PR #71

- private spreadsheet CSV import and immutable-run validation;
- resumable private JSON sessions outside the catalogue;
- correct, background/unknown, false and duplicate classifications;
- direct missed-face geometry authoring;
- per-photo completion arithmetic; and
- spreadsheet-compatible export.

### Large-image refinement — merged in PR #72

- Fit, 100%, 200% and 400% source-pixel zoom;
- scrollable panning and full-width focus mode; and
- stable normalized detector and missed-face geometry at every zoom level.

### Repeated-run comparison — merged in PR #74

- reusable face-level ground truth frozen from the completed baseline;
- exact candidate-source validation by filename and SHA-256;
- deterministic intersection-over-union connected-component matching;
- automatic handling of clean one-to-one matches;
- exception review for unmatched, duplicate and ambiguous components;
- resumable private correction and gate-assessment storage;
- overall, five-plus-face, source-group, category and M16 gate summaries; and
- spreadsheet-compatible summary export.

### Comparison-review clarity — merged in PRs #76 and #77

- one exception photo at a time with previous, next and save-and-next actions;
- operator-facing candidate/reference terminology and decision-completion status;
- distinct pass, fail, pending, resolved and needs-review treatment;
- compact numbered `R` and `C` markers for clustered faces;
- removal of internal component IDs from the operator workflow; and
- automatic detector-miss classification when a photo has reference faces but no candidate review boxes.

The delivered comparison capability is covered by synthetic integration tests for frozen ground truth, isolated catalogue attachment, changed-source rejection, correction persistence, restart recovery, metrics and gate export.

## Active viewport-workspace implementation

WI-0040 draft PR #79 currently implements:

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

The implementation preserves the existing comparison API, correction format, automatic-miss rules, matching, completion arithmetic, metrics and exports. Automated validation covers view-state reset, the published zoom/pan helper and responsive workspace styles. GitHub Actions and privacy-safe human review with representative portrait, landscape and multi-decision photos remain pending before completion.

## Active next work

WI-0035 continues the fixed confidence sweep against the unchanged 100-photo set. WI-0040 is now in progress and should be completed before substantial remaining manual exception review when practical; it does not change the governed threshold order or candidate-run preparation.

The immediate experiment steps remain:

1. start the application with the immutable confidence-0.9 catalogue and freeze its reusable ground truth;
2. process the unchanged sample at confidence `0.8` into a new isolated catalogue and output directory;
3. attach the `0.8` processing run to the frozen ground truth;
4. resolve only the surfaced exceptions and record the material-category assessment;
5. export and assess the M16 gate; and
6. stop when the complete gate passes, otherwise continue in order with `0.7`, `0.6` and `0.5` as governed by the recorded result.

The complete Windows commands and comparison workflow are documented in [`docs/operations/detector-comparison-runs.md`](../../operations/detector-comparison-runs.md).

## Privacy

No private image names, source paths, face boxes, counts, ground-truth files, databases or detector outputs are committed. Repository evidence records only the fixed method, completed review, failed baseline gate, delivered comparison capability, privacy-safe usability findings and active governed work.
