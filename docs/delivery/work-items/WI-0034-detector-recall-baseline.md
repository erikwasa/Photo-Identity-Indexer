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

## Completed baseline

The maintainer staged the fixed private 100-photo set, retained the 50 representative pilot photos, supplemented the difficult half with archive-relevant photos, and processed the set with the pinned YuNet detector at confidence `0.9` in an isolated catalogue.

The complete baseline was reviewed through the detector-evaluation workspace on Windows PowerShell 5.1. The private session contains the per-photo classifications, missed-face geometry, notes and source-group/category metadata. Private filenames, paths, face boxes and detailed counts remain outside Git.

On 2026-08-05 the completed confidence-0.9 baseline was evaluated against the predeclared M16 decision target and **did not pass**. This is a measurement result, not an implementation failure. The immutable baseline session is now the reusable ground truth for WI-0035.

Implementation status and scope are recorded in [M16 detector evaluation workspace status](../status/M16-detector-evaluation-workspace.md).

## Acceptance criteria

- [x] The 100 photos are unique and selected before reviewing detector results.
- [x] Every photo has countable, correctly detected, missed, false and duplicate totals.
- [x] Per-photo arithmetic is checked before aggregation.
- [x] Overall, source-group and category recall are calculated.
- [x] The reusable photo-level review and ground-truth path from WI-0039 is used.
- [x] Detector evaluation remains separate from identity assignment and rejection history.
- [x] The M16 decision target is evaluated without changing it after seeing the result.
- [x] Only privacy-safe completion and gate evidence is added to repository evidence.

## Gate result

The confidence-0.9 baseline failed the M16 gate. Continue to [WI-0035](WI-0035-yunet-threshold-sweep.md) using the exact same immutable photos, counting rule and authored ground truth. Do not rerun or edit the baseline merely to improve the result.
