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

## Next checkpoints

1. Survey the maintained dHash/CLIP extraction and neighbour retrieval paths; instrument model provenance, exact-search complexity, archive-scale storage/query cost and unrelated-date/person candidate recall before adding third-party models.
2. Export bounded private candidate-neighbour pairs; inspect archive coverage and near-copy group leakage; freeze pair assignment and create a genuinely blinded private review UI.
3. Review calibration pairs, predeclare conservative model/threshold precision targets and separately authorize one held-out redundancy evaluation.
4. Decide whether WI-0195 may proceed with a quality-aware collection simulation; do not add production persistence or suppression in M37.
