# Local evaluation workflow

This specialized runbook evaluates one exact detector/embedder revision from an already reviewed local catalogue. Use the [local operator guide](local-operator-guide.md) for setup, processing, browser review, suggestion regeneration, collections, backup and cleanup.

For a same-corpus comparison between model revisions, use the [multi-model comparison workflow](multi-model-comparison.md).

## Evaluation boundary

An accepted evaluation identifies and fixes:

- the canonical SQLite catalogue;
- immutable source and asset revisions;
- detector model ID and SHA-256;
- embedder model ID and SHA-256;
- alignment and preprocessing contracts;
- dataset ID and pipeline version;
- split seed and split counts; and
- the processing run that produced the selected embeddings.

Human-confirmed assignments are canonical. Suggestions and evaluation outputs are derived, model-versioned evidence.

## 1. Define private paths and exact revisions

Run from the repository root:

```powershell
$root = "C:\PhotoIdentityPilot"
$db = Join-Path $root "catalogue.db"
$evaluation = Join-Path $root "model-lab"
$runId = "REPLACE_WITH_RUN_ID"

New-Item -ItemType Directory -Force -Path $evaluation | Out-Null

$detector = Get-Content `
  .\models\manifests\yunet-2023mar-fp32.json -Raw | ConvertFrom-Json
$embedder = Get-Content `
  .\models\manifests\sface-2021dec-fp32.json -Raw | ConvertFrom-Json
```

Confirm the saved batch run before export:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch status --database $db --run $runId
```

Expected success signals:

- the run reports the intended detector and embedder IDs;
- the intended immutable revisions are complete or have recorded unsupported/unavailable outcomes; and
- the selected model files match their pinned manifests.

## 2. Regenerate suggestions for the exact embedder

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  match regenerate `
  --database $db `
  --embedder-id $embedder.modelId `
  --embedder-hash $embedder.sha256
```

Expected success signals:

- the exact model ID and hash are echoed;
- target and suggestion counts are reported;
- confirmed assignments and append-only review history remain unchanged; and
- rejected face-person pairs remain excluded.

Regeneration prepares exact-model advisory evidence; it does not create canonical labels.

## 3. Export a deterministic reviewed-catalogue split

Define output and fixed provenance:

```powershell
$manifest = Join-Path $evaluation "baseline.json"
$report = Join-Path $evaluation "baseline-report.json"
$datasetId = "private-baseline-v1"
$pipelineVersion = "local-pipeline-v1"
$splitSeed = "private-baseline-split-v1"
```

Export:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate export `
  --database $db `
  --output $manifest `
  --dataset-id $datasetId `
  --pipeline-version $pipelineVersion `
  --detector-id $detector.modelId `
  --detector-hash $detector.sha256 `
  --embedder-id $embedder.modelId `
  --embedder-hash $embedder.sha256 `
  --seed $splitSeed `
  --run $runId
```

Expected success signals:

- export reports the exact detector and embedder revisions;
- the source and processing-run scope is explicit;
- gallery, validation and held-out test splits are created under the fixed seed; and
- no private path or report is written under the repository.

## 4. Evaluate without changing the split

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset $manifest `
  --output $report
```

Validation selects thresholds. The held-out test split reports final results and must not select a replacement threshold.

Expected success signals:

- the report preserves dataset and exact-model provenance;
- validation and held-out results are separated;
- unknown-rejection and identification results are present; and
- the command completes without changing the canonical catalogue.

## 5. Prove deterministic bytes

Repeat export and evaluation with unchanged inputs, then compare hashes:

```powershell
Get-FileHash $manifest -Algorithm SHA256
Get-FileHash $report -Algorithm SHA256
```

The repeated manifest and report hashes must match their previous values. A mismatch means the input scope, provenance, ordering or implementation changed and must be investigated before comparing results.

## 6. Interpret and retain evidence

Record private evidence outside Git:

- exact detector and embedder IDs and hashes;
- immutable revision and evaluated-face counts;
- split identity and selected thresholds;
- held-out identification and unknown-rejection metrics;
- confusion and representative error review;
- processing/evaluation throughput when measured; and
- defects, uncertainty and recommendation.

Do not commit the catalogue, images, crops, embeddings, names, real manifests, reports or per-person results.

## Multi-model evaluation

Do not duplicate this procedure manually for a candidate model. The [multi-model comparison workflow](multi-model-comparison.md) automates fixed-scope processing, exact-model suggestion regeneration, deterministic export/evaluation, split equality checks, resumability and private summary generation.

## Related references

- [Local operator guide](local-operator-guide.md)
- [Multi-model comparison workflow](multi-model-comparison.md)
- [Evaluation method](../models/evaluation-method.md)
- [Model manifests and governance](../models/model-governance.md)
- [Recognition and identity matching](../architecture/identity-matching.md)
- [Glossary](../glossary.md)
