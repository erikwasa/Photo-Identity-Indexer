---
id: WI-0022
title: Select production models
milestone: M11
status_source: ../status/work-items.yaml
depends_on: [WI-0021, WI-0030]
affected_modules: [tools/model-lab, docs/models, docs/delivery]
---

# WI-0022: Select production models

## Historical objective

The original plan was to select production detector/embedder revisions, thresholds and processing profile using held-out local comparison plus measured Azure evidence.

## Retirement — 2026-09-12

The Azure-dependent acceptance contract is retired. The production archive already uses the governed local configuration documented by the recognition/operator material: CenterFace `centerface-2019-fp32` at confidence `0.5`, the governed single-pass detector pipeline, SFace five-point alignment and `sface-2021dec-fp32` embedding.

ADR-0010 removes Azure consistency and cloud-cost evidence as production gates. Future model changes remain governed, evidence-driven local model work rather than a continuation of this historical milestone.

The canonical status is an administrative closeout; it does not claim the original Azure comparison criteria were executed.

## Historical acceptance criteria

- Selection uses fixed gallery, validation and held-out test splits.
- Precision, known recall, unknown rejection and difficult-category confusion meet agreed targets.
- Model licences, hashes, dimensions and pipeline versions are recorded.
- Local and Azure execution agree within tolerance.
- Runtime, storage and cost projections fit the available budget.
- The decision identifies rejected alternatives and the evidence supporting the choice.
