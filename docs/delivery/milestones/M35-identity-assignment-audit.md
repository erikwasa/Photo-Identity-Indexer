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

- [x] The maintainer can audit a bounded date range of automatic assignments without selecting people one at a time.
- [x] Results are grouped clearly by assigned person, preserve correction/history navigation, and remain usable at catalogue scale.
- [x] Automatic-assignment score, margin and exact-model provenance come from the accepted assignment evidence rather than the current suggestion state.

## Maintainer acceptance — 2026-10-04 (Europe/Stockholm)

After PRs #506 and #507 and the date-only filter follow-up in PR #508, the maintainer re-verified the maintained `/audit` workflow and reports WI-0185 works as expected. Date-range filtering, cross-person browsing, provenance/order, paging and correction/return-context behavior are accepted. M35 is complete.
