---
id: WI-0127
title: Evaluate whole-image embeddings for semantic Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0120]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Core.Tests, PhotoIdentity.Persistence.Tests, docs]
---

# WI-0127: Evaluate whole-image embeddings for semantic Creative Collections

## Objective

Evaluate model-versioned whole-image embeddings as derived evidence for semantic search, visual similarity and diversity in Creative Collections.

## Why

Whole-image embeddings could unlock queries and comparisons that tags cannot express well, including visually similar scenes, natural-language semantic anchors and more nuanced redundancy/diversity. They also introduce materially more model, storage and indexing complexity, so usefulness should be demonstrated before productionizing them.

## In scope

- Define a regenerable PhotoEmbedding contract tied to immutable revision, exact model/hash and preprocessing profile.
- Evaluate one or more practical local image/text embedding models using review proxies first where suitable.
- Measure usefulness for semantic retrieval, similar-photo discovery and Creative Collection diversity on a private representative sample.
- Start with exact/bounded vector comparison and add pgvector/ANN only if measured scale requires it.
- Compare incremental value against metadata-only selection and controlled-vocabulary tagging where available.
- Produce a go/no-go recommendation and a bounded production integration plan if justified.

## Out of scope

- Reusing face embeddings as whole-image embeddings.
- External embedding APIs receiving private photos.
- Adding ANN/vector infrastructure before measured retrieval requirements justify it.

## Acceptance criteria

- [ ] Whole-image embeddings are explicitly derived/versioned and never canonical photo metadata.
- [ ] A reproducible local experiment measures retrieval usefulness and runtime/storage cost.
- [ ] The experiment includes at least semantic retrieval, similar-photo retrieval and a Creative Collection selection/diversity comparison.
- [ ] Exact retrieval is measured before any ANN/index dependency is selected.
- [ ] The outcome records whether whole-image embeddings materially outperform cheaper metadata/tag approaches enough to justify production work.

## Verification requirements

Automated smoke tests for any prototype evidence/retrieval contract plus maintainer review of aggregate evaluation findings and representative private results.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
