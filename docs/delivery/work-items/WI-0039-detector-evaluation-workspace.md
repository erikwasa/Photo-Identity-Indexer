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

### Slice 3: repeated-run comparison — active

The failed baseline makes the comparison slice required before WI-0035 runs the remaining confidence thresholds.

This slice must:

- derive reusable countable-face ground truth from correct/background detections and manually marked misses;
- attach a later detector run to the same private manifest and immutable source revisions;
- match later detections to ground-truth faces with deterministic intersection-over-union rules;
- identify one-to-one matches, misses, unmatched detections, duplicates and ambiguous overlaps;
- require human review only for unmatched or ambiguous cases;
- preserve manual corrections in a private resumable comparison session; and
- export source-group, category, overall and M16 decision-gate summaries.

## Acceptance criteria

- [x] A selected run lists all of its photos, including zero-detection photos.
- [x] Original photos and detector boxes render together without exposing private source paths.
- [x] The workspace does not write identity assignments, rejections or suggestions.
- [x] Synthetic integration tests cover run scoping, stable ordering, bounding boxes, privacy and original-photo streaming.
- [x] Private sample metadata can be imported and validated without being committed.
- [x] Review progress, classifications and missed-face geometry survive an application restart.
- [x] Spreadsheet-compatible per-photo results can be exported.
- [x] Large source photos can be inspected and annotated at source-pixel zoom without changing normalized geometry.
- [ ] Face-level ground truth can be reused across later threshold and detector runs.
- [ ] Automatic matching is deterministic and surfaces ambiguous cases for human review.
- [ ] Category, source-group and M16 gate summaries can be exported for a candidate run.

## Privacy

Photos, filenames, source paths, databases, detector outputs, ground-truth geometry and manual judgements remain outside Git. Tests use synthetic data only. Private session files default to the application-local detector-evaluation directory and can be redirected with `PhotoIdentity__DetectorEvaluationRoot`.

## Completion boundary

WI-0034 is complete and established that confidence `0.9` does not meet the M16 target. WI-0035 remains blocked until this work item can compare candidate runs against the immutable baseline ground truth without a complete manual recount.
