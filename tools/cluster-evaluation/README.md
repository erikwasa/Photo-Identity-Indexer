# Private provisional face-cluster evaluation

This tooling exists only for WI-0113 evaluation. It does not create production clusters or canonical identity assignments.

## Privacy boundary

The exported JSON contains face embeddings and is biometric/private data. Keep the sample and generated reports under `private/`, `data/`, or another location outside the repository. Never commit them.

The C# exporter intentionally omits source paths, filenames, crop paths, person names, and raw catalogue identifiers from the file. Canonical person IDs are converted to deterministic local labels such as `person-0001`; face and photo IDs are similarly replaced with local sample labels.

## 1. Export one exact-model reviewed sample

Set the same local PostgreSQL connection string used by Photo Identity:

```powershell
$env:PhotoIdentity__Postgres__ConnectionString = "Host=localhost;Port=5432;Database=photoidentity;Username=...;Password=..."
```

Then export the production embedding model revision. The default output is already ignored by Git:

```powershell
dotnet run --project tools/PhotoIdentity.ClusterEvaluation -- `
  --model-id sface-2021dec-fp32 `
  --model-hash <exact-model-sha256> `
  --max-faces 5000 `
  --output private/cluster-evaluation/sample.json
```

Assigned faces are labelled by canonical person for evaluation. Canonical Unknown faces are included by default as unlabeled discovery points; pass `--exclude-unknown` only for a controlled comparison. Rejected and unreviewed faces are not used as ground-truth rows in this reviewed sample.

## 2. Create an isolated Python environment

```powershell
py -m venv private/cluster-evaluation/.venv
& private/cluster-evaluation/.venv/Scripts/Activate.ps1
python -m pip install -r tools/cluster-evaluation/requirements.txt
```

The evaluator uses scikit-learn's DBSCAN and HDBSCAN implementations plus a conservative mutual-neighbour graph implementation. All algorithms receive the same precomputed cosine-distance matrix.

## 3. Evaluate

```powershell
python tools/cluster-evaluation/evaluate.py `
  private/cluster-evaluation/sample.json `
  --report-json private/cluster-evaluation/report.json `
  --report-md private/cluster-evaluation/report.md `
  --expected-face-count 10000
```

The default grid intentionally explores conservative neighbourhoods. Override the comma-separated grids when the first pass shows that the useful decision boundary lies elsewhere.

The report separates:

- false-merge pairs and false-merge rate (primary risk),
- false-split pairs/rate,
- total and labelled coverage,
- noise/singleton rate and cluster-size distribution,
- same-photo merges between different reviewed identities,
- the measured pairwise cosine-distance time and a rough quadratic projection to the expected archive scale.

Unknown faces affect discovery coverage/noise but are excluded from identity-labelled false-merge/false-split denominators because `Unknown` is not a person identity.

## 4. Record the WI-0113 decision

Do not automatically copy the evaluator's first-ranked candidate into production. Inspect the private report, especially every false merge and same-photo conflict. Record:

- the chosen algorithm and policy/thresholds,
- why its false-merge risk is acceptable relative to split/noise cost,
- observations across age, pose, and image-quality variation present in the sample,
- the handling policy for Unknown/noise/outliers,
- whether measured local neighbour-query cost supports exact PostgreSQL/vector search for WI-0114 or justifies an ANN index.

The committed repository may contain the resulting non-personal policy conclusion and aggregate metrics, but never the sample, embeddings, crops, face/person mappings, or any private per-face report.
