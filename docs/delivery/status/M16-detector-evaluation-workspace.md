# M16 detector evaluation workspace status

Status date: 2026-08-03

Current implementation branch: `agent/WI-0039-ground-truth-authoring`

## Why this work started

The fixed 100-photo evaluation set is ready and the isolated YuNet confidence-0.9 batch has completed. The identity review queue is face-oriented: it shows aligned crops, omits complete photo context and cannot show photos with zero detections. That makes category-based detector recall review unnecessarily slow and incomplete across repeated threshold runs.

## Accepted direction

Build a detector-specific, photo-level workspace that remains separate from canonical identity review.

The durable target is:

1. browse every photo for one processing run in stable order;
2. show the original source photo with persisted normalized detector boxes and confidence labels;
3. retain zero-detection photos;
4. import private Sample ID, Sample Group, Source Group, Primary Category and countable-face ground truth outside Git;
5. record reusable face-level ground-truth geometry;
6. automatically match later detector runs to ground truth; and
7. export privacy-safe aggregate and category comparisons.

Detector evaluation decisions must not create identity assignments, identity rejections or synthetic people.

## Slice 1 merged

Pull request [#70](https://github.com/erikwasa/Photo-Identity-Indexer/pull/70) delivered the read-only photo browser:

- photo-level run queries and no-cache API endpoints;
- original-photo streaming without source paths;
- `/detector-evaluation` processing-run selection and overlays;
- stable ordering including zero-detection photos; and
- synthetic integration coverage for privacy, geometry and streaming.

## Slice 2 in progress

The current branch adds the private authoring loop:

- parse Excel-exported comma or semicolon CSV after optional workbook preamble rows;
- validate the manifest against every immutable photo in the selected run;
- optionally verify full source SHA-256 values;
- create resumable private JSON sessions outside the catalogue;
- classify detections as correct, background/unknown, false or duplicate;
- mark missed countable faces directly on the source image;
- enforce per-photo arithmetic before marking a row complete;
- resume after application restart; and
- export spreadsheet-compatible CSV rows.

The default private location is the application-local `detector-evaluations` directory. Operators can set `PhotoIdentity__DetectorEvaluationRoot` to keep the session files beside other private M16 evidence.

## Still pending

A final comparison slice must:

- derive reusable countable-face ground truth from the authored session;
- match later threshold or detector runs with deterministic intersection-over-union rules;
- surface unmatched and ambiguous cases for human correction; and
- calculate source-group, category and M16 decision-gate summaries.

## Privacy

No private image names, source paths, face boxes, counts, ground-truth files, databases or detector outputs are committed. Only implementation, automated synthetic tests and privacy-safe progress notes belong in Git.
