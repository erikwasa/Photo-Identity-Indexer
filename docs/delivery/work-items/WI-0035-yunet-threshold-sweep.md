---
id: WI-0035
title: Tune YuNet confidence threshold
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0034, WI-0039]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Worker, PhotoIdentity.Api, PhotoIdentity.Web, Evaluation]
---

# WI-0035: Tune YuNet confidence threshold

## Objective

Determine whether confidence tuning alone can meet the M16 recall target while preserving acceptable false-detection effort.

## Activation

The fully reviewed YuNet confidence-0.9 baseline from WI-0034 did not meet the predeclared M16 decision target on 2026-08-05. Threshold tuning was therefore selected as the first detector experiment.

The `0.9` run remains immutable. PR [#74](https://github.com/erikwasa/Photo-Identity-Indexer/pull/74) completed the reusable comparison workflow, and WI-0040 completed the viewport-fitted exception-review workspace. Every threshold candidate reused the frozen baseline ground truth and surfaced only unmatched, duplicate or ambiguous cases for review.

The Windows procedure remains documented in [Detector comparison runs](../../operations/detector-comparison-runs.md).

## Final outcome

The maintainer completed isolated comparison runs for confidence `0.8`, `0.7`, `0.6` and `0.5` against the unchanged confidence-0.9 ground truth by 2026-08-06. Every candidate was fully reviewed and every complete M16 gate failed.

The baseline and all governed threshold candidates are retained as durable private evidence. Detailed counts, filenames, paths, images and category values remain private.

No confidence threshold is approved for rollout. Threshold tuning is insufficient, so WI-0036 multi-scale YuNet is activated.

## Scope

- Reprocess the exact WI-0034 sample in isolated databases and output directories.
- Evaluate the fixed threshold grid: `0.8`, `0.7`, `0.6` and `0.5`.
- Keep model, source revisions, preprocessing, padding and counting rules fixed.
- Reuse the immutable confidence-0.9 face-level ground truth for automatic comparison.
- Compare recall, false detections, duplicates, background/unknown workload and review effort.
- Review only unmatched, duplicate or ambiguous geometry unless a comparison invariant fails.
- Do not write threshold experiments into the canonical reviewed catalogue.

## Reproducibility

Every threshold run retained:

- the same source-manifest hash;
- the same 100 staged photos;
- the exact YuNet model identifier and SHA-256;
- the confidence and padding values;
- the repository commit used to run the experiment;
- a separate database, output directory, log and configuration record; and
- a private comparison session tied to the same baseline ground truth.

## Acceptance criteria

- [x] Confidence `0.8` used the same 100 photos and face-counting ground truth.
- [x] The `0.8` run retained exact configuration and model provenance.
- [x] Automatic matching preserved baseline face-level ground truth for `0.8`.
- [x] The `0.8` unmatched and ambiguous cases were reviewed without recounting every photo.
- [x] Every remaining governed threshold used the same 100 photos and face-counting ground truth.
- [x] Each remaining run recorded exact configuration and model provenance.
- [x] The threshold decision was based on the predeclared M16 decision target.
- [x] Existing reviewed identities were not changed by threshold experiments.
- [x] A privacy-safe final comparison summary identifies that threshold tuning is insufficient.

## Gate result

Every governed confidence from `0.9` through `0.5` failed the complete M16 gate. Preserve all private evidence, close WI-0035 and continue to WI-0036.
