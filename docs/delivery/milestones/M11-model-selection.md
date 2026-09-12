---
id: M11
title: Production model selection
status_source: ../status/milestones.yaml
depends_on: [M08, M10]
---

# M11: Production model selection

## Historical outcome

This milestone originally required selecting detector, embedder, thresholds and processing profile from local multi-model evidence plus measured Azure execution evidence.

## Work items

- [WI-0022](../work-items/WI-0022-model-selection.md)

## Retirement — 2026-09-12

The Azure-dependent selection contract is retired. The production archive already operates with the governed local configuration documented by the recognition/operations material: CenterFace `centerface-2019-fp32` at confidence `0.5`, the governed single-pass detector pipeline, SFace five-point alignment and `sface-2021dec-fp32` embedding.

[ADR-0010](../../decisions/ADR-0010-local-production-execution.md) removes local/Azure consistency and cloud-cost evidence as production gates. WI-0022 is therefore closed administratively rather than rerun under an obsolete acceptance contract. Future production-model changes remain governed model/recognition decisions and require their own measured evidence.

## Historical exit criteria

The original criteria are retained for context but are not current gates:

- Accuracy meets the agreed precision and unknown-rejection targets.
- Difficult relatives, age gaps and low-quality categories are inspected.
- Licences, model hashes and pipeline versions are recorded.
- Local and Azure results are consistent within tolerance.
- Runtime and cost fit the available budget or are divided into controlled batches.
