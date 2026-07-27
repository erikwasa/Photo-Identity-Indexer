---
id: M11
title: Production model selection
status_source: ../status/milestones.yaml
depends_on: [M08, M10]
---

# M11: Production model selection

## Outcome

A detector, embedder, thresholds and processing profile are selected using the local multi-model evidence plus measured Azure execution evidence when resources are available.

## Work items

- [WI-0022](../work-items/WI-0022-model-selection.md)

## Exit criteria

- Accuracy meets the agreed precision and unknown-rejection targets.
- Difficult relatives, age gaps and low-quality categories are inspected.
- Licences, model hashes and pipeline versions are recorded.
- Local and Azure results are consistent within tolerance.
- Runtime and cost fit the available budget or are divided into controlled batches.
