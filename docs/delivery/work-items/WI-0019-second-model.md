---
id: WI-0019
title: Add a second model adapter
milestone: M08
status_source: ../status/work-items.yaml
depends_on: [WI-0017, WI-0018]
affected_modules: [PhotoIdentity.Recognition.Onnx, tools/model-lab]
---

# WI-0019: Add a second model adapter

## Objective

Integrate at least one additional detector or embedder through the same neutral contracts and compare it with the baseline.

## Acceptance criteria

- [ ] Existing labels and people remain unchanged.
- [ ] Embeddings coexist by model ID.
- [ ] The same evaluation set compares both models.
- [ ] Licence and model hashes are documented.
