---
id: M35
title: Identity assignment audit and calibration
status_source: ../status/milestones.yaml
depends_on: []
---

# M35: Identity assignment audit and calibration

## Outcome

Automatic identity assignments can be reviewed efficiently across the whole catalogue, with enough assignment-time provenance to spot mistakes and tune automatic-assignment thresholds safely.

## Work items

- [WI-0185](../work-items/WI-0185-automatic-assignment-audit.md) - redesign identity audit around cross-person automatic assignments.

## Exit criteria

- [ ] The maintainer can audit a bounded time range of automatic assignments without selecting people one at a time.
- [ ] Results are grouped clearly by assigned person, preserve correction/history navigation, and remain usable at catalogue scale.
- [ ] Automatic-assignment score, margin and exact-model provenance come from the accepted assignment evidence rather than the current suggestion state.
