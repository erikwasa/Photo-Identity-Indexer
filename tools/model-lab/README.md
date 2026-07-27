# Model lab evaluation datasets

The model lab uses a versioned JSON manifest with three explicit identity-evaluation splits:

- `gallery` contains human-confirmed exemplars used for matching;
- `validation` is the only split used to select an identity threshold;
- `test` is held out until the threshold has been selected and is used only for final reporting.

Do not reuse a face or sample identifier across splits. Personal images, crops, embeddings, identity identifiers and reports remain local and must not be committed. The checked-in example is synthetic.

## Current command

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

## Catalogue export plan

The current `evaluate` command reads a prepared manifest. WI-0028 will add a deterministic export from a reviewed SQLite catalogue with:

- explicit catalogue, model revision and run or photo scope;
- stable gallery, validation and test assignment;
- source-photo grouping to prevent split leakage;
- exact model and pipeline provenance; and
- clear errors when the reviewed data cannot support meaningful known and unknown splits.

The formal 500-image pilot begins only after this export exists.

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

Each validation or test sample records a stable sample ID, expected person or unknown state, face expectation and detection outcome, an embedding when available and measured elapsed milliseconds.

## Threshold policy

For each configured threshold, the harness performs an exact cosine scan and scores each gallery person by their best exemplar. The selected threshold maximises the validation split's average of known-person identification recall and unknown rejection rate.

Ties prefer higher identification precision, then higher unknown rejection, then the higher threshold. The test split is evaluated after selection and cannot influence the chosen threshold.

## Reported metrics

The deterministic report includes detector recall, identification precision, known-person recall, unknown rejection, balanced identity score, confusion rows, validation and test sweeps, images per second and optional archive runtime and cost projections.

Threshold selection does not imply automatic acceptance in the product. Suggestions remain review-only.
