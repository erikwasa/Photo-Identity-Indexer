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

The current review application is optimized for assigning individual aligned face crops. Detector evaluation instead needs the complete source photo, all boxes from one exact processing run, and explicit visibility of photos where the detector found nothing. Reconstructing photo context from the face queue is slow and cannot support repeated confidence or detector comparisons efficiently.

## Delivery slices

### Slice 1: read-only photo browser

- Query processing runs and every processing-job photo in stable filename order.
- Include successful photos with zero detections.
- Return normalized persisted detector boxes and confidence values.
- Serve original photos through the existing path-safe resolver.
- Add a local browser page with run selection, pagination and overlays.
- Keep the API free of source roots and private storage paths.

### Slice 2: private evaluation manifest

- Import Sample ID, Source Group, Primary Category and Countable Faces from a private file outside Git.
- Key evaluation rows by source hash and sample ID rather than filename alone.
- Validate that the manifest and selected processing run refer to the same immutable photos.

### Slice 3: reusable ground truth and comparison

- Record countable-face geometry and optional background/unknown disposition.
- Classify detections as correct, background/unknown, false or duplicate.
- Mark missed faces directly on the source photo.
- Match later detector runs automatically with deterministic intersection-over-union rules.
- Require human review only for unmatched or ambiguous cases.
- Export source-group, category and overall metrics in a spreadsheet-compatible format.

## Acceptance criteria

- [ ] A selected run lists all of its photos, including zero-detection photos.
- [ ] Original photos and detector boxes render together without exposing private source paths.
- [ ] The workspace does not write identity assignments, rejections or suggestions.
- [ ] Synthetic integration tests cover run scoping, stable ordering, bounding boxes, privacy and original-photo streaming.
- [ ] Private sample metadata can be imported and validated without being committed.
- [ ] Face-level ground truth can be reused across later threshold and detector runs.
- [ ] Automatic matching is deterministic and surfaces ambiguous cases for human review.
- [ ] Category and source-group summaries can be exported for the M16 decision gate.

## Privacy

Photos, filenames, source paths, databases, detector outputs, ground-truth geometry and manual judgements remain outside Git. Tests use synthetic data only.

## Completion boundary

WI-0034 remains blocked on the reusable review path until the operator can complete the baseline tally without reconstructing photo context from individual crops. WI-0035 must use the same private manifest and ground truth when the baseline gate fails.
