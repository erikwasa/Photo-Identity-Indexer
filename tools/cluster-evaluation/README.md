# Private provisional face-cluster evaluation

This tooling supports the private reviewed-data evaluation for WI-0113 and WI-0116. It never creates production clusters or canonical identity assignments.

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

The evaluators use scikit-learn over the exported exact-model embeddings.

## 3. WI-0113 clustering-policy evaluation

```powershell
python tools/cluster-evaluation/evaluate.py `
  private/cluster-evaluation/sample.json `
  --report-json private/cluster-evaluation/report.json `
  --report-md private/cluster-evaluation/report.md `
  --expected-face-count 10000
```

The default grid intentionally explores conservative neighbourhoods. Override the comma-separated grids when the first pass shows that the useful decision boundary lies elsewhere.

The WI-0113 report separates false merges, false splits, coverage/noise, same-photo conflicts and rough scaling cost. Unknown faces affect discovery coverage/noise but are excluded from identity-labelled false-merge/false-split denominators because `Unknown` is not a person identity.

## 4. WI-0116 cluster-assisted known-person advisory evaluation

Use the same private sample to evaluate the fixed production DBSCAN policy together with the initial advisory rule:

```powershell
python tools/cluster-evaluation/evaluate_advisory.py `
  private/cluster-evaluation/sample.json `
  --report-json private/cluster-evaluation/advisory-report.json `
  --report-md private/cluster-evaluation/advisory-report.md
```

The WI-0116 evaluator intentionally fixes clustering to the production defaults (`eps=0.30`, `min_samples=3`) and evaluates advisory identity evidence separately from canonical assignment. For each sample face it simulates ordinary known-person ranking by taking the best cosine similarity to another reviewed exemplar of each Person; the target face itself is excluded so a face cannot vote for itself.

The committed production advisory rule starts conservatively:

- a member vote must meet the existing ordinary Medium score threshold (default `0.50`),
- at least 3 independent members must favor the same Person,
- those votes must cover at least 60% of the discovered cluster,
- Core members must cover at least 60% of the cluster,
- a competing Person with more than 1 qualifying vote or more than 20% cluster support makes the result ambiguous,
- production `not same` evidence also forces ambiguity/fail-closed behavior; the WI-0113 export does not contain those constraints, so that path is covered by automated production tests instead.

The private report records:

- Strong / Ambiguous / Insufficient advisory counts,
- false-person proposals and conservative proposal precision,
- recall over pure reviewed clusters with enough labelled members to form a policy opportunity,
- how many reviewed mixed clusters incorrectly reach Strong,
- an estimated review-task compression relative to reviewing those opportunity faces one-by-one.

A Strong proposal counts as correct only when every reviewed member in that discovered cluster has one canonical identity and the advisory candidate matches it. This deliberately penalizes mixed-cluster proposals instead of crediting a majority vote.

The evaluator does not alter the production suggestion thresholds and does not evaluate or enable automatic assignment. Review-effort figures are comparative action-count estimates rather than measured operator time.

## 5. Record decisions without committing private data

For WI-0113, record the selected clustering policy and aggregate evaluation conclusion. For WI-0116, record only aggregate advisory precision/recall/review-effort results and the maintainer decision about whether the initial advisory thresholds are acceptable.

The committed repository may contain non-personal aggregate metrics and policy conclusions, but never the sample, embeddings, crops, face/person mappings, or private per-face/per-cluster reports.
