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

### Slice 2: private manifest and ground-truth authoring — in progress

- Import Sample ID, Sample Group, Source Group, Primary Category and Countable Faces from a private CSV outside Git.
- Match the manifest to one immutable processing run by exact staged filename and optional source SHA-256.
- Persist resumable private JSON session files outside the canonical catalogue.
- Classify every persisted detection as correct, background/unknown, false or duplicate.
- Mark missed countable faces directly on the source photo.
- Enforce `countable = correct/background + missed` before a photo is complete.
- Export spreadsheet-compatible per-photo CSV rows.

### Slice 3: repeated-run comparison — pending

- Derive reusable countable-face ground truth from correct/background detections and manually marked misses.
- Match later detector runs automatically with deterministic intersection-over-union rules.
- Require human review only for unmatched or ambiguous cases.
- Export source-group, category and overall comparison summaries for the M16 decision gate.

## Acceptance criteria

- [x] A selected run lists all of its photos, including zero-detection photos.
- [x] Original photos and detector boxes render together without exposing private source paths.
- [x] The workspace does not write identity assignments, rejections or suggestions.
- [x] Synthetic integration tests cover run scoping, stable ordering, bounding boxes, privacy and original-photo streaming.
- [ ] Private sample metadata can be imported and validated without being committed.
- [ ] Review progress, classifications and missed-face geometry survive an application restart.
- [ ] Spreadsheet-compatible per-photo results can be exported.
- [ ] Face-level ground truth can be reused across later threshold and detector runs.
- [ ] Automatic matching is deterministic and surfaces ambiguous cases for human review.
- [ ] Category and source-group summaries can be exported for the M16 decision gate.

## Privacy

Photos, filenames, source paths, databases, detector outputs, ground-truth geometry and manual judgements remain outside Git. Tests use synthetic data only. Private session files default to the application-local detector-evaluation directory and can be redirected with `PhotoIdentity__DetectorEvaluationRoot`.

## Completion boundary

WI-0034 remains blocked until the operator can author and resume the complete baseline ground truth without reconstructing photo context from individual crops. WI-0035 must use the same private manifest and ground truth when the baseline gate fails.
