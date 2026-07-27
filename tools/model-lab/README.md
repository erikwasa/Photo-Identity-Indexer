# Model lab evaluation datasets

The model lab uses a versioned JSON manifest with three explicit identity-evaluation splits:

- `gallery` contains human-confirmed exemplars used for matching;
- `validation` is the only split used to select an identity threshold;
- `test` is held out until the threshold has been selected and is used only for final reporting.

Do not reuse a face or sample identifier across splits. Personal images, crops and embeddings remain local and must not be committed. The checked-in example is synthetic.

## Run an evaluation

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset tools/model-lab/example-dataset.json `
  --output .artifacts/model-lab/example-report.json `
  --archive-images 100000 `
  --hourly-cost 1.50 `
  --currency GBP
```

Fixed input produces byte-for-byte identical report JSON. The report contains an SHA-256 digest of the complete input manifest rather than local file paths.

## Dataset schema

Top-level fields:

- `schemaVersion`: currently `1`;
- `datasetId`: stable operator-defined dataset identifier;
- `pipelineVersion`: version for decoding, detection, alignment and embedding policy;
- `detector`: exact detector model ID and SHA-256 hash;
- `embedder`: exact embedding model ID, SHA-256 hash and dimensions;
- `thresholds`: unique cosine thresholds from `-1` through `1`;
- `gallery`: confirmed exemplars with `faceId`, `personId` and embedding;
- `validation`: known and unknown samples used to choose the threshold;
- `test`: known and unknown held-out samples used for final metrics.

Each validation or test sample records:

- a stable `sampleId`;
- `expectedPersonId`, or `null` for an unknown person or no-face image;
- whether a face is expected and whether one was detected;
- an embedding when a face was detected;
- positive measured `elapsedMilliseconds` for throughput projection.

A known sample must reference a person represented in the gallery. Every split must contain at least one known and one unknown sample so identification and unknown-rejection metrics are both meaningful.

## Threshold policy

For each configured threshold, the harness performs an exact cosine scan and scores each gallery person by their best exemplar. The selected threshold maximises the validation split's average of:

1. known-person identification recall; and
2. unknown rejection rate.

Ties prefer higher identification precision, then higher unknown rejection, then the higher threshold. The test split is evaluated after selection and cannot influence the chosen threshold.

## Reported metrics

The deterministic report includes:

- detector recall;
- identification precision across accepted predictions;
- known-person identification recall;
- unknown rejection rate;
- balanced identity score;
- confusion rows for known people, unknowns, rejections and detector misses;
- validation and test threshold sweeps;
- measured images per second;
- optional archive hours and compute cost from the held-out test throughput.

Threshold selection does not imply automatic acceptance in the product. Suggestions remain review-only. False-accept and false-reject interpretation must be documented for each real evaluation dataset.
