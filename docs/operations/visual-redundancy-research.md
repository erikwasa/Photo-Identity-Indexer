# WI-0194 — archive-wide visual redundancy research

**Checkpoint 1: review-pair sampling protocol only.** No model selection, human pair review or archive-wide similarity export yet. This work is independent of the consumed WI-0193 human-quality held-out cohort: never reuse WI-0193 held-out labels.

## Labels and conservative decision

The human label is a **pair** decision: Redundant (normally only one wanted in a generated collection), Similar but keep both, Not similar, or Unsure. Exact file copies, transformed near copies and broad semantic resemblance remain distinct. Future automatic suppression needs >=95% observed precision together with conservative uncertainty reporting; false positives are more costly than retaining repetitive photos. Insufficient evidence means no-go or manual-only.

## Checkpoint 1 — offline blind-pair preparation

The standard-library Python file tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_review_pairs.py accepts an **existing private** input manifest of photo identities and candidate similarity pairs. It DOES NOT compute hashes, CLIP embeddings or nearest neighbours; these are separate WI-0194 steps.

The private JSON input schema has five required keys: schemaVersion (1), protocol (wi0194-candidate-pairs-v1), sampleId (arbitrary private unique string), photos, pairs.

Each photos element has revisionId and a 64-hex-digit contentSha256; optional leakageGroup co-partitions known near duplicates.

Each pairs element has leftRevisionId, rightRevisionId, sourceKind (retrieved or random-negative); optional integer dhashDistance [0,64] and finite clipCosine [-1,1]. A retrieved pair must have at least one similarity measure. Unknown fields such as ratings/reviews, private paths and duplicate/reversed pairs are prohibited. Smaller dHash indicates more perceptual similarity; higher CLIP cosine suggests more shared visual/semantic content. The numeric scores do NOT establish redundancy labels.

The sampler unites known exact-content photos, supplied leakageGroup families and pairs with dHash distance <=4 before assigning a deterministic photo-disjoint hash split (approximately 75% calibration / 25% held-out at the family level). It never selects cross-partition pairs. Fixed bounded sample buckets: exact copy control, near-copy, high, medium, borderline, dHash/CLIP disagreement, and explicit random negatives. The bucket cutoffs are **research sampling heuristics only**: not precision evidence, calibrated thresholds or production policy. dHash<=4 grouping can overcluster; unlinked near copies can leak. Real candidate recall must be inspected before freezing any cohort.

Two **new private, exclusive-write** exports are produced. The blind manifest contains only pseudonymous pair IDs, revision references and label choices; it deliberately excludes scores, model provenance, bucket, partition and photo group. The private audit contains source fingerprints, bucket counts, photo partitions and evidence needed for a later controlled calibration. Console output contains aggregate counts only. Status is research-review-set-draft-not-frozen. No human pair labels may be accessed until a separate reviewer and freeze protocol exist.

### Synthetic-only local verification after merge

    cd "C:\Kod\codex\Photo Identity Indexer"
    git switch main
    git pull --ff-only origin main
    $python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
    & $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p test_redundancy_review_pairs.py -v

The future CLI (NOT an instruction to run it on a real archive before candidate export and cohort review):

    & $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_review_pairs.py `
      --input private/wi0194-neighbour-candidates.json `
      --blind-output private/wi0194-blind-pairs.json `
      --audit-output private/wi0194-pair-audit.json

Never commit or share private source, pair, score, review or audit exports. Preserve the independent WI-0194 held-out seal and do not tune after viewing held-out pair ratings.

## Checkpoint 2 — bounded current-catalogue candidate export (implementation ready; local run pending)

The new .NET command reads **current photo revisions** with existing durable review proxies from PostgreSQL. It verifies local proxy content hashes, computes the existing OpenCV 64-bit dHash, optionally reuses **exact-version** CLIP vectors already indexed for semantic search, and computes exact similarity neighbours without a date, moment, people, location or folder predicate. It never opens originals, triggers cloud hydration, downloads a model, generates new CLIP embeddings, runs a migration or changes the catalogue.

**Scope limits:** the safe default is **500** proxy-backed revisions, capped at **5,000**; this is only a deterministic archive-spread subset, not representative or archive-wide neighbour recall. Every within-subset dHash pair is examined, including all links with dHash Hamming distance at most four (for leakage protection only). Exact CLIP comparisons scan the indexed sampled corpus only for a bounded number of deterministic seeds. Neither scan proves accuracy or recall outside the sample. CLIP semantic similarity is not visual redundancy ground truth.

Use Windows PowerShell from the repository root, with the *existing* PhotoIdentity PostgreSQL connection and review-proxy root/profile environment settings used for the quality benchmark:

~~~powershell
git switch main
git pull --ff-only origin main
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- redundancy export --output private/wi0194-neighbour-candidates.json --report private/wi0194-neighbour-metrics.json --max-photos 500 --neighbors 8
~~~

The command above intentionally tests dHash without CLIP. To include CLIP, first inspect the existing model ID, model SHA256 and preprocessing version values in the **authoritative** PostgreSQL photo_semantic_embeddings table; do not guess them or use a historical restore. Append all the following options to the same command:

~~~powershell
--clip-model-id "INDEXED_MODEL_ID" --clip-model-sha256 "EXACT_64_HEX_SHA256" --clip-preprocessing "INDEXED_PREPROCESSING_VERSION" --clip-seeds 100
~~~

All three provenance options must be provided together. CLIP indexing coverage in the sampled cohort is reported explicitly; zero coverage is not a model accuracy result. Existing CLIP generation timings and actual checkpoint size/license still require separate evidence before candidate selection.

Two **new, exclusively created** JSON files under private/ are produced: a candidate-pair manifest compatible with the checkpoint-1 blind sampler, and an aggregate measurements report. Outputs never overwrite existing files. The stdout report contains only aggregate counts, scan comparisons and timing, never identifiers or similarity values. The exported private pair evidence is **not** a human-reviewed set and **not** a frozen hold-out; inspect population/coverage and potential near-copy leakage before using the blind sampler. Never commit private exports.

### Local verification

~~~powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj --filter FullyQualifiedName~RedundancyCandidateExportTests
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
~~~

Share back only the aggregate stdout: proxy-backed population, sampled photos, CLIP coverage, scan counts, candidate-pair counts, timings, and whether known near-copies from different dates appear. Do not share paths, photo IDs, SQL credentials, full source manifests or per-pair scores. Real archive results have **not** been obtained in this checkpoint. No thresholds or production suppression authorized.

## Next checkpoints

1. Survey the maintained dHash/CLIP extraction and neighbour retrieval paths; instrument model provenance, exact-search complexity, archive-scale storage/query cost and unrelated-date/person candidate recall before adding third-party models.
2. Export bounded private candidate-neighbour pairs; inspect archive coverage and near-copy group leakage; freeze pair assignment and create a genuinely blinded private review UI.
3. Review calibration pairs, predeclare conservative model/threshold precision targets and separately authorize one held-out redundancy evaluation.
4. Decide whether WI-0195 may proceed with a quality-aware collection simulation; do not add production persistence or suppression in M37.
