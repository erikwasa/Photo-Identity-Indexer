---
id: WI-0127
title: Evaluate whole-image embeddings for semantic Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0120]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Cli, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Core.Tests, PhotoIdentity.Recognition.Tests, PhotoIdentity.Integration.Tests, docs]
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

- [x] Whole-image embeddings are explicitly derived/versioned and never canonical photo metadata.
- [x] A reproducible local experiment measures retrieval usefulness and runtime/storage cost.
- [x] The experiment includes at least semantic retrieval, similar-photo retrieval and a Creative Collection selection/diversity comparison.
- [x] Exact retrieval is measured before any ANN/index dependency is selected.
- [ ] The outcome records whether whole-image embeddings materially outperform cheaper metadata/tag approaches enough to justify production work.

## Verification requirements

Automated smoke tests for any prototype evidence/retrieval contract plus maintainer review of aggregate evaluation findings and representative private results.

## Implementation status

- `PhotoEmbeddingEvidence` is a regenerable derived-evidence contract tied to immutable revision, exact model SHA-256, preprocessing version, vector encoding and dimension. The experiment never writes this evidence to canonical photo metadata.
- The evaluator reuses the pinned full CLIP ViT-B/32 model from WI-0126 but reads normalized `image_embeds` and `text_embeds` directly instead of converting results into controlled-vocabulary labels.
- Review proxies remain the only image input for the bounded experiment. Embeddings are held in memory and discarded when the process exits.
- Semantic text-to-image retrieval and image-to-image nearest-neighbor retrieval use exact cosine comparison over at most 500 sampled Creative candidates. No pgvector, HNSW, IVF or other ANN/index dependency is introduced.
- The ordinary metadata-first Creative selector remains unchanged by default. The evaluator can opt into `m26-image-embedding-diversity-v1`, which adds bounded penalties for candidates highly similar to already-selected images.
- The aggregate JSON report records model/tokenizer provenance, embedding dimension, runtime, raw storage projection, exact-comparison counts and selection-diversity metrics. Query text, revision ids, filenames and paths are omitted.
- Optional private review output copies only durable review proxies and creates a local HTML page covering baseline-vs-embedding selection, text retrieval and similar-photo retrieval.
- A representative private run and maintainer visual judgement are still required before a production go/no-go decision.

## Completion notes

- Files changed: derived photo-embedding contract and exact cosine utility, opt-in Creative embedding-diversity scoring, CLIP dual-encoder adapter, bounded PostgreSQL CLI evaluator, private visual review output, unit/integration tests and the operator evaluation runbook.
- Trade-offs: the prototype deliberately performs exact in-memory comparisons and repeats inference per run; that is appropriate for usefulness measurement but not a production storage/index design.
- Deferred work: representative private retrieval/similarity/diversity review, final go/no-go decision, and any production persistence/index design only if the experiment demonstrates material value.
- Commands run: repository CI will provide build/test/documentation evidence; model- and photo-dependent acceptance remains a maintainer-run private experiment.
