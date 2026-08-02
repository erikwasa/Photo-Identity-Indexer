# Model manifests and governance

Every detector, aligner and embedder used by Photo Identity Indexer has an immutable checked-in descriptor. Model binaries remain outside Git and are accepted locally only after size and SHA-256 verification.

## Required manifest identity

A governed model descriptor records:

- stable model ID and role;
- upstream source and pinned revision;
- file name, byte size, format and SHA-256;
- input dimensions, colour order and numeric type;
- scale, mean and other normalisation;
- alignment protocol when applicable;
- output dimensions and adapter-owned normalisation;
- distance or scoring metric;
- runtime compatibility;
- code and model-weight licences; and
- known training-data or redistribution considerations.

A model ID alone is not exact provenance. Derived results must record both model ID and exact hash.

## When model identity changes

Create a different model ID or revision when any material behavior changes, including:

- weights or graph bytes;
- quantisation;
- preprocessing or colour order;
- input dimensions or numeric type;
- alignment protocol;
- output normalisation or distance metric; or
- runtime behavior that changes produced results materially.

Do not overwrite a manifest to point an existing ID at different bytes.

## Installation

Install pinned files into ignored local storage:

```powershell
./models/install-models.ps1
```

Install one model explicitly:

```powershell
./models/install-models.ps1 -Id sface-2021dec-int8
```

Verify the local installation:

```powershell
./verify-local.ps1
```

Expected success signals:

- the manifest is valid for the requested role;
- the downloaded or existing file matches the pinned size and SHA-256; and
- an unavailable or mismatched file fails closed rather than being substituted silently.

Large model files must not be committed.

## Runtime selection and resume

Processing runs persist selected detector and embedder IDs. Resume loads the persisted run configuration and must not silently switch models.

The resolved local model file must still match the checked-in manifest. A missing or mismatched model is an operational error, not permission to use another file.

## Persisted provenance

Detector observations, embeddings, suggestions, evaluation exports, reports and bundle results retain exact model provenance.

Baseline and candidate embeddings can coexist for the same face occurrence. Matching and suggestion operations select one exact embedder revision. Scores from different revisions are never mixed or assumed to share thresholds.

Removing a local model file prevents future inference with that revision but does not invalidate persisted provenance or erase canonical review data.

## Human review boundary

Models produce observations and advisory suggestions. They do not own people or canonical labels.

- only human-confirmed assignments become exemplars;
- suggestions never become assignments automatically;
- rejected face-person pairs are retained;
- model replacement does not rewrite review history; and
- promotion of a default model requires evaluation plus human acceptance evidence.

## Comparison and promotion

Use the [multi-model comparison workflow](../operations/multi-model-comparison.md) to compare candidates under fixed source, detector, alignment, review and evaluation scope.

A recommendation considers:

- held-out identification and unknown rejection;
- representative confusion/disagreement review;
- suggestion usefulness and correction effort;
- deterministic and exact provenance;
- throughput and storage;
- runtime/deployment consistency;
- licence and redistribution constraints; and
- remaining uncertainty.

The accepted local comparison retains `sface-2021dec-fp32` as the current default and keeps `sface-2021dec-int8` as a governed candidate. Final production selection remains deferred pending later evidence.

## Licence policy

Do not assume a source-code repository licence automatically covers downloaded pretrained weights or training data.

Record separately:

- code licence;
- distributed model-file licence;
- upstream source and immutable revision;
- available training-data statements; and
- redistribution or usage concerns.

Unclear provenance blocks production promotion even when local experiments are technically successful.

## Optional remote compute

Portable bundles include the exact model manifests required by a worker. The worker validates local model bytes before processing and returns exact provenance with results.

Azure or another worker environment cannot choose a replacement revision implicitly. Consistency differences between local and remote execution are evaluation evidence and may create a new governed runtime/model revision when material.

See [Baseline models](baseline-models.md), [Candidate models](candidate-models.md), [Recognition and identity matching](../architecture/identity-matching.md) and the [Glossary](../glossary.md).
