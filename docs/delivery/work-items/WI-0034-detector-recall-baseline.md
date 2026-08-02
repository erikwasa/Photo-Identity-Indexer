---
id: WI-0034
title: Measure baseline detector recall
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0029]
affected_modules: [Documentation, Human verification]
---

# WI-0034: Measure baseline detector recall

## Objective

Measure the current pinned YuNet pipeline on 100 private pilot photos without reviewing the complete archive.

## Procedure

Follow [Detector recall pilot](../../operations/detector-recall-pilot.md). Use 50 mechanically selected representative photos and 50 deliberately difficult photos, apply one fixed face-counting rule, and retain only privacy-safe aggregate evidence.

## Acceptance criteria

- [ ] The 100 photos are unique and selected before reviewing detector results.
- [ ] Every photo has countable, correctly detected, missed, false and duplicate totals.
- [ ] Per-photo arithmetic is checked before aggregation.
- [ ] Overall and category recall are calculated.
- [ ] The M16 decision target is evaluated without changing it after seeing the result.
- [ ] Only the privacy-safe aggregate summary is added to repository evidence.

## Gate

When the decision target passes, cancel WI-0035 through WI-0038 and complete M16. Otherwise continue to WI-0035.
