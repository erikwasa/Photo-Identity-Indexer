---
id: WI-0035
title: Tune YuNet confidence threshold
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0034]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Worker, Evaluation]
---

# WI-0035: Tune YuNet confidence threshold

## Objective

Determine whether confidence tuning alone can meet the M16 recall target while preserving acceptable false-detection effort.

## Scope

- Reprocess the exact WI-0034 sample in isolated databases and output directories.
- Evaluate a fixed threshold grid, initially `0.9`, `0.8`, `0.7`, `0.6` and `0.5`.
- Keep model, source revisions, preprocessing and counting rules fixed.
- Compare recall, false detections, duplicates and review effort.
- Do not write threshold experiments into the canonical reviewed catalogue.

## Acceptance criteria

- [ ] Every threshold uses the same 100 photos and face-counting ground truth.
- [ ] Each run records exact configuration and model provenance.
- [ ] The selected threshold is based on the predeclared M16 decision target.
- [ ] Existing reviewed identities are not changed by the experiment.
- [ ] A privacy-safe comparison summary identifies whether threshold tuning is sufficient.

## Gate

When a threshold-only candidate passes, cancel WI-0036 and WI-0037 and continue to WI-0038. Otherwise continue to WI-0036.
