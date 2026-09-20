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
- [x] The outcome records whether whole-image embeddings materially outperform cheaper metadata/tag approaches enough to justify production work.

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
- The representative private run and maintainer visual review are complete. The result is split: CLIP whole-image embeddings are promising for semantic text-to-photo retrieval, but they did not demonstrate sufficient similar-photo or Creative Collection diversity value to justify production vector infrastructure in M26.

## Completion notes

- Implementation evidence: PR #381 added the derived/versioned embedding contract, CLIP image/text embedding adapter, exact retrieval evaluator, experimental embedding-diversity selector, aggregate report and private visual review page.
- Private sample: 185 Creative Collection candidates were sampled and all 185 durable review proxies embedded successfully, with zero unavailable proxies and zero decode failures.
- Runtime/storage: 512-dimensional float32 embeddings averaged 64.6 ms per proxy (63.5 ms median, 76.8 ms p95). Raw vector storage is 2,048 bytes per photo, or about 204.8 MB per 100,000 photos before database/index overhead.
- Semantic retrieval: eight natural-language queries were evaluated with exact cosine search. For the seven queries individually scored in the maintainer note, 7/8 or 8/8 top results were judged relevant. This is materially more useful than WI-0126's fixed controlled-vocabulary labels and demonstrates a credible future semantic-search use case.
- Similar-photo retrieval: only two of four reviewed seeds produced meaningfully related neighbors. The other two seeds did not, so the evidence is not strong enough for a production similar-photo feature.
- Creative diversity: the embedding policy replaced 8 of 50 baseline photos, but the maintainer judged the replacements as merely different rather than better or less repetitive. Mean pairwise cosine moved only from 0.5371 to 0.5354 and p95 from 0.7119 to 0.7008.
- Decision: **no-go for embedding-based Creative Collection diversity and no-go for adding pgvector/ANN/vector persistence to M26**. Keep the metadata/presentation-first selector unchanged.
- Separate opportunity: **semantic text-to-photo retrieval is promising enough to preserve as a future product direction**, but it should be evaluated/productized as its own search capability rather than used as justification for heavier Creative Collection infrastructure.
- Exact search was sufficient for the bounded evaluation; no ANN index was used and no catalogue writes occurred.
- Production boundary: do not persist whole-image embeddings or add vector infrastructure as part of M26. If semantic search is later prioritized, start from the existing versioned evidence contract and measure archive-scale query latency/storage before selecting persistence/index technology.
- Deferred work: a dedicated semantic-search work item may be created later if that product direction is prioritized; no additional WI-0127 implementation is required.
- Commands run: PR #381 CI plus the maintainer's private `image-embeddings evaluate` run on the representative collection.
