---
id: WI-0019
title: Add a second model adapter
milestone: M08
status_source: ../status/work-items.yaml
depends_on: [WI-0017, WI-0018, WI-0029, WI-0033]
affected_modules: [PhotoIdentity.Recognition.Onnx, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Worker, tools/model-lab]
---

# WI-0019: Add a second model adapter

## Objective

Integrate at least one additional detector or embedder through the neutral model contracts after the baseline 500-image workflow is proven.

## Acceptance criteria

- [ ] Existing people, labels and review actions remain unchanged.
- [ ] Baseline and candidate embeddings coexist by model ID and exact model hash.
- [ ] The same immutable source revisions can be processed by both models without overwriting results.
- [ ] The same evaluation set can be exported for both models.
- [ ] Licence, source, input contract, dimensions and model hashes are documented.
- [ ] Failure or removal of the candidate adapter does not make baseline results unreadable.

## Scope guidance

Prefer one additional embedder first unless detector comparison is needed to answer a specific pilot finding. A narrow second adapter is more valuable than adding several uncalibrated models at once.
