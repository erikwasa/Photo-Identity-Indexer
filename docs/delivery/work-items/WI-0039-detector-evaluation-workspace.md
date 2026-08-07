---
id: WI-0039
title: Build detector evaluation workspace
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0029]
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests]
---

# WI-0039: Build detector evaluation workspace

## Objective

Make repeated detector-recall evaluation photo-oriented, category-aware and reusable without changing canonical identity review state.

## Problem

The identity review application is optimized for assigning individual aligned face crops. Detector evaluation instead needs the complete source photo, all boxes from one exact processing run, and explicit visibility of photos where the detector found nothing. Reconstructing photo context from the face queue is slow and cannot support repeated confidence or detector comparisons efficiently.

## Delivery slices

### Slice 1: read-only photo browser — merged

Pull request [#70](https://github.com/erikwasa/Photo-Identity-Indexer/pull/70) added:

- processing-run and photo-level SQLite queries;
- stable filename ordering including successful zero-detection photos;
- normalized persisted detector boxes and confidence values;
- original-photo streaming through the path-safe resolver;
- `/detector-evaluation` with run selection, pagination and overlays; and
- synthetic privacy, ordering, geometry and streaming coverage.

### Slice 2: private manifest and ground-truth authoring — merged

Pull request [#71](https://github.com/erikwasa/Photo-Identity-Indexer/pull/71) added:

- private comma- or semicolon-separated manifest import after optional spreadsheet preamble rows;
- exact matching to one immutable processing run by filename and optional SHA-256;
- resumable private JSON sessions outside the canonical catalogue;
- correct, background/unknown, false and duplicate classifications;
- direct missed-face geometry authoring;
- per-photo arithmetic validation;
- restart/resume support; and
- spreadsheet-compatible per-photo CSV export.

Pull request [#72](https://github.com/erikwasa/Photo-Identity-Indexer/pull/72) added source-pixel zoom, scrolling and focus mode so small faces in large source photos can be marked accurately without changing saved session data.

The complete private confidence-0.9 baseline was reviewed through these merged slices and did not meet the M16 decision target on 2026-08-05.

### Slice 3: repeated-run comparison — merged

Pull request [#74](https://github.com/erikwasa/Photo-Identity-Indexer/pull/74) completed the comparison workflow:

- freezes reusable countable-face ground truth from the completed baseline;
- verifies candidate catalogues against the exact frozen filename and SHA-256 source set;
- matches later detections with deterministic intersection-over-union connected components;
- hides clean one-to-one matches and surfaces unmatched, duplicate and ambiguous components;
- persists manual corrections and qualitative gate assessment outside the catalogue;
- reports overall, five-plus-face, source-group, category and M16 gate summaries; and
- exports spreadsheet-compatible summaries while retaining private detailed evidence.

Synthetic integration coverage verifies ground-truth freezing, isolated candidate attachment, changed-source rejection, correction persistence, restart recovery, metrics and gate export.

### Follow-up: neutral candidate detections

Later detector review showed that a stronger detector can legitimately find faces that were intentionally outside the fixed countable-face ground truth. Treating every such unmatched candidate as a false detection can penalize a detector for useful extra detections rather than actual false positives.

The comparison workflow therefore supports a `Neutral` candidate outcome with deliberately narrow semantics:

- the candidate detection must be a legitimate face detection that is outside the fixed countable-face scope;
- neutral resolves the candidate review item but is not a match and therefore cannot increase recall;
- neutral is excluded from the false-plus-duplicate M16 penalty;
- a countable reference face that remains unmatched must still be resolved as a detector miss; and
- neutral totals are retained in comparison summaries and CSV exports so historical corrections remain auditable.

Existing private comparison JSON remains compatible because the new neutral collection defaults to empty when absent. Previous comparisons can therefore be reopened and corrected without regenerating detector runs or frozen ground truth.

## Acceptance criteria

- [x] A selected run lists all of its photos, including zero-detection photos.
- [x] Original photos and detector boxes render together without exposing private source paths.
- [x] The workspace does not write identity assignments, rejections or suggestions.
- [x] Synthetic integration tests cover run scoping, stable ordering, bounding boxes, privacy and original-photo streaming.
- [x] Private sample metadata can be imported and validated without being committed.
- [x] Review progress, classifications and missed-face geometry survive an application restart.
- [x] Spreadsheet-compatible per-photo results can be exported.
- [x] Large source photos can be inspected and annotated at source-pixel zoom without changing normalized geometry.
- [x] Face-level ground truth can be reused across later threshold and detector runs.
- [x] Automatic matching is deterministic and surfaces ambiguous cases for human review.
- [x] Category, source-group and M16 gate summaries can be exported for a candidate run.
- [x] Legitimate out-of-scope candidate faces can be marked neutral without changing recall or false/duplicate gate arithmetic.

## Privacy

Photos, filenames, source paths, databases, detector outputs, ground-truth geometry and manual judgements remain outside Git. Tests use synthetic data only. Private session files default to the application-local detector-evaluation directory and can be redirected with `PhotoIdentity__DetectorEvaluationRoot`.

## Completion boundary

WI-0039 is complete after PR #74 merged into `main` on 2026-08-05. Later evaluation-workspace corrections, including neutral candidate disposition support, preserve that completed scope while improving the reusable M16 review tooling. WI-0035 through WI-0037 consume this workspace for governed detector comparisons.
