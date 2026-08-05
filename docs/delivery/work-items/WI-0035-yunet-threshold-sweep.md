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

The fully reviewed YuNet confidence-0.9 baseline from WI-0034 did not meet the predeclared M16 decision target on 2026-08-05. Threshold tuning is therefore the selected next detector experiment.

The `0.9` run is complete and remains immutable. Do not repeat it. PR [#74](https://github.com/erikwasa/Photo-Identity-Indexer/pull/74) completed WI-0039, so the remaining threshold sweep can now reuse the frozen baseline ground truth and surface only unmatched, duplicate or ambiguous cases for review.

Start with confidence `0.8` in an isolated catalogue. Compare and assess that candidate before deciding whether to continue with `0.7`, `0.6` and `0.5`. Follow the Windows procedure in [Detector comparison runs](../../operations/detector-comparison-runs.md).

## Scope

- Reprocess the exact WI-0034 sample in isolated databases and output directories.
- Evaluate the remaining fixed threshold grid: `0.8`, `0.7`, `0.6` and `0.5`.
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

- [ ] Every threshold uses the same 100 photos and face-counting ground truth.
- [ ] Each run records exact configuration and model provenance.
- [ ] Automatic matching is deterministic and preserves baseline face-level ground truth.
- [ ] Unmatched and ambiguous cases can be reviewed without recounting every photo.
- [ ] The selected threshold is based on the predeclared M16 decision target.
- [ ] Existing reviewed identities are not changed by the experiment.
- [ ] A privacy-safe comparison summary identifies whether threshold tuning is sufficient.

## Gate

When a threshold-only candidate passes, cancel WI-0036 and WI-0037 and continue to WI-0038. Otherwise continue to WI-0036 after all governed threshold candidates have failed or the recorded evidence shows that lower thresholds cannot resolve the material failure category acceptably.
