---
id: M05
title: Identity matching
status_source: ../status/milestones.yaml
depends_on: [M01, M04]
---

# M05: Identity matching

## Outcome

Confirmed examples generate ranked, model-versioned identity suggestions for unlabelled faces.

## Work items

- [WI-0016](../work-items/WI-0016-matcher.md)

## Exit criteria

- Suggestions record best and second-best scores and their margin.
- Rejected pairs are not immediately repeated.
- Suggestions never become canonical labels automatically.
- Only human-confirmed faces become exemplars.
