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

## Current work

Draft pull request [#33](https://github.com/erikwasa/Photo-Identity-Indexer/pull/33) adds an exact local cosine matcher over one explicit embedding model revision. Current active human assignments and legacy `confirmed` labels provide exemplars; undone assignments, non-confirmed labels and merged people are excluded.

Each target records at most the best and second-best distinct people in deterministic score order, including their score margin. Rejected face-person pairs remain durable exclusions, and regeneration does not write or alter canonical labels or review actions.

The first implementation deliberately uses exact scanning to establish correctness. Approximate indexing, threshold calibration and measured false-accept/false-reject performance remain later work.
