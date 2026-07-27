---
id: WI-0022
title: Select production models
milestone: M11
status_source: ../status/work-items.yaml
depends_on: [WI-0021, WI-0030]
affected_modules: [tools/model-lab, docs/models, docs/delivery]
---

# WI-0022: Select production models

## Objective

Select the production detector, embedder, thresholds and processing profile from the held-out local comparison and measured Azure evidence.

## Acceptance criteria

- [ ] Selection uses fixed gallery, validation and held-out test splits.
- [ ] Precision, known recall, unknown rejection and difficult-category confusion meet agreed targets.
- [ ] Model licences, hashes, dimensions and pipeline versions are recorded.
- [ ] Local and Azure execution agree within tolerance.
- [ ] Runtime, storage and cost projections fit the available budget.
- [ ] The decision identifies rejected alternatives and the evidence supporting the choice.
