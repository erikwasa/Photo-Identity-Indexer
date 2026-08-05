# M16 detector evaluation workspace status

Status date: 2026-08-05

Status update branch: `agent/M16-baseline-failed-status`

## Current outcome

The fixed private 100-photo set has been processed and fully reviewed with the pinned YuNet detector at confidence `0.9`. The detector-evaluation workspace retained the complete photo context, zero-detection photos, per-detection classifications, missed-face geometry, source groups and primary categories without changing canonical identity review state.

The completed confidence-0.9 baseline was evaluated against the predeclared M16 decision target on 2026-08-05 and **did not pass**. Detailed counts, filenames, paths, images and category values remain private. The immutable authored session is the reference ground truth for the next threshold experiments.

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

These slices were verified locally against the private confidence-0.9 run on Windows PowerShell 5.1.

## Active next slice

The failed baseline activates the repeated-run comparison slice of WI-0039 before WI-0035 executes confidence `0.8`, `0.7`, `0.6` and `0.5`.

The comparison slice must:

- derive reusable countable-face ground truth from the authored baseline;
- attach candidate runs to the same immutable photos and private manifest;
- use deterministic intersection-over-union matching;
- classify matches, misses, unmatched detections, duplicates and ambiguous overlaps;
- present only exceptions for human correction;
- persist resumable private comparison decisions; and
- calculate overall, five-plus-face, source-group, category and M16 gate summaries.

WI-0034 is complete. WI-0035 is the selected next detector experiment but remains blocked until the comparison slice is usable.

## Privacy

No private image names, source paths, face boxes, counts, ground-truth files, databases or detector outputs are committed. Repository evidence records only the fixed method, completed review, failed gate and implementation state.
