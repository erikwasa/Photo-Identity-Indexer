# M16 detector evaluation workspace status

Status date: 2026-08-03

Implementation branch: `agent/WI-0039-detector-evaluation-workspace`

## Why this work started

The fixed 100-photo evaluation set is ready and the isolated YuNet confidence-0.9 batch has completed. The existing identity review queue is face-oriented: it shows aligned crops, omits complete photo context and cannot show photos with zero detections. That makes category-based detector recall review unnecessarily slow and incomplete across repeated threshold runs.

## Accepted direction

Build a detector-specific, photo-level workspace that remains separate from canonical identity review.

The durable target is:

1. browse every photo for one processing run in stable order;
2. show the original source photo with persisted normalized detector boxes and confidence labels;
3. retain zero-detection photos;
4. import private Sample ID, Source Group, Primary Category and countable-face ground truth outside Git;
5. record reusable face-level ground-truth geometry;
6. automatically match later detector runs to ground truth; and
7. export privacy-safe aggregate and category comparisons.

Detector evaluation decisions must not create identity assignments, identity rejections or synthetic people.

## First implementation slice

In progress on the branch:

- add read-only SQLite queries for processing runs and photo-level detections;
- expose no-cache API endpoints without source roots or private storage paths;
- reuse the existing path-safe resolver to stream the original photo;
- add `/detector-evaluation` with processing-run selection, stable filename ordering, pagination and bounding-box overlays;
- include successful photos with no detections; and
- add integration coverage for privacy, zero-detection photos, bounding boxes and original-photo streaming.

## Deferred within the same direction

The first slice intentionally does not yet persist evaluation decisions. Follow-up implementation should add:

- private manifest import for Sample ID, Source Group, Primary Category and Countable Faces;
- per-detection classifications: correct, background/unknown, false and duplicate;
- direct marking of missed-face ground truth on the original image;
- immutable private JSON export keyed by source hash and sample ID;
- automatic intersection-over-union matching for later runs; and
- category summaries and spreadsheet-compatible export.

## Privacy

No private image names, source paths, face boxes, counts, ground-truth files, databases or detector outputs are committed. Only implementation, automated synthetic tests and privacy-safe progress notes belong in Git.
