---
id: WI-0034
title: Measure baseline detector recall
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0029, WI-0039]
affected_modules: [Human verification]
---

# WI-0034: Measure baseline detector recall

## Objective

Measure the current pinned YuNet pipeline on 100 private pilot photos without reviewing the complete archive.

## Procedure

Follow [Detector recall pilot](../../operations/detector-recall-pilot.md). Use 50 mechanically selected representative photos and 50 deliberately difficult photos, apply one fixed face-counting rule, and retain only privacy-safe aggregate evidence.

The accepted sample design retains the 50 representative pilot photos. The difficult half may include archive-relevant photos outside the original pilot when the pilot lacks important face conditions. Record every row as `Pilot representative`, `Pilot difficult` or `External difficult`, keep the exact 100-photo directory immutable across runs and report source-group results separately.

## Current execution note

The maintainer has staged the fixed 100-photo set, retained the 50 representative pilot photos, added approximately 20 external difficult photos, and completed the isolated YuNet confidence-0.9 batch on Windows PowerShell 5.1. Private filenames, paths and per-photo counts remain outside Git.

The identity-oriented face queue is not an acceptable detector-recall review surface. It hides photos with zero detections and requires the operator to reconstruct photo context from individual aligned crops. [WI-0039](WI-0039-detector-evaluation-workspace.md) therefore provides the reusable photo-level review and ground-truth path required to finish this work item and any later threshold comparison.

Implementation status and scope are recorded in [M16 detector evaluation workspace status](../status/M16-detector-evaluation-workspace.md).

## Acceptance criteria

- [ ] The 100 photos are unique and selected before reviewing detector results.
- [ ] Every photo has countable, correctly detected, missed, false and duplicate totals.
- [ ] Per-photo arithmetic is checked before aggregation.
- [ ] Overall, source-group and category recall are calculated.
- [ ] The reusable photo-level review and ground-truth path from WI-0039 is used.
- [ ] Detector evaluation remains separate from identity assignment and rejection history.
- [ ] The M16 decision target is evaluated without changing it after seeing the result.
- [ ] Only the privacy-safe aggregate summary is added to repository evidence.

## Gate

When the decision target passes, cancel WI-0035 through WI-0038 and complete M16. Otherwise continue to WI-0035 using the same immutable photos and reusable ground truth.
