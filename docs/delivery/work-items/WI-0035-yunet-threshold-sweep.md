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

The fully reviewed YuNet confidence-0.9 baseline from WI-0034 did not meet the predeclared M16 decision target on 2026-08-05. Threshold tuning is therefore the selected detector experiment.

The `0.9` run is complete and remains immutable. Do not repeat it. PR [#74](https://github.com/erikwasa/Photo-Identity-Indexer/pull/74) completed the reusable comparison workflow, and WI-0040 completed the viewport-fitted exception-review workspace. The remaining threshold sweep reuses the frozen baseline ground truth and surfaces only unmatched, duplicate or ambiguous cases for review.

Follow the Windows procedure in [Detector comparison runs](../../operations/detector-comparison-runs.md).

## Current progress

Confidence `0.8` was processed in an isolated catalogue, compared against the frozen confidence-0.9 ground truth and fully reviewed by the maintainer on 2026-08-05. The candidate **failed the complete M16 gate**. Detailed counts and category evidence remain private.

The governed next candidate is confidence `0.7`. Do not rerun `0.8`; preserve its catalogue, logs, private comparison and export as the durable failed-candidate record.

## Scope

- Reprocess the exact WI-0034 sample in isolated databases and output directories.
- Evaluate the fixed threshold grid: `0.8`, `0.7`, `0.6` and `0.5`.
- Keep model, source revisions, preprocessing, padding and counting rules fixed.
- Reuse the immutable confidence-0.9 ground-truth session for automatic comparison.
- Compare recall, false detections, duplicates, background/unknown workload and review effort.
- Review only unmatched, duplicate or ambiguous geometry unless a comparison invariant fails.
- Do not write threshold experiments into the canonical reviewed catalogue.

## Reproducibility

Every threshold run must retain:

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
- [ ] Every remaining governed threshold uses the same 100 photos and face-counting ground truth.
- [ ] Each remaining run records exact configuration and model provenance.
- [ ] The selected threshold is based on the predeclared M16 decision target.
- [x] Existing reviewed identities were not changed by the `0.8` experiment.
- [ ] A privacy-safe final comparison summary identifies whether threshold tuning is sufficient.

## Gate

When a threshold-only candidate passes, cancel WI-0036 and WI-0037 and continue to WI-0038. Confidence `0.8` failed, so continue with `0.7`. If the remaining governed threshold candidates also fail, or recorded evidence shows that lower thresholds cannot resolve the material failure category acceptably, continue to WI-0036.
