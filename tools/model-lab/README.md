# Model lab evaluation datasets

The model lab uses a versioned JSON manifest with three explicit identity-evaluation splits:

- `gallery` contains human-confirmed exemplars used for matching;
- `validation` is the only split used to select an identity threshold;
- `test` is held out until the threshold has been selected and is used only for final reporting.

Do not reuse a face or source revision across splits. Personal images, crops, embeddings, identity identifiers, real manifests and reports remain local and must not be committed. The checked-in example is synthetic.

## Export from a reviewed catalogue

Use `evaluate export` to create the manifest directly from active human assignments in a local SQLite catalogue. Select exact detector and embedder revisions and exactly one scope: a processing run or one or more immutable asset revisions.

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate export `
  --database C:\PhotoIdentity\catalogue.db `
  --output C:\PhotoIdentity\private-evaluation\baseline.json `
  --dataset-id private-baseline-v1 `
  --pipeline-version local-pipeline-v1 `
  --detector-id yunet `
  --detector-hash DETECTOR_SHA256 `
  --embedder-id sface `
  --embedder-hash EMBEDDER_SHA256 `
  --seed private-baseline-split-v1 `
  --run PROCESSING_RUN_ID
```

For an explicit photo-revision scope, replace `--run` with one or more `--revision ASSET_REVISION_ID` options.

The defaults require one gallery, validation and test photo per known person plus one unknown photo in each held-out split. Increase these with `--gallery-per-person`, `--validation-known-per-person`, `--test-known-per-person`, `--validation-unknown` and `--test-unknown`. Repeated `--threshold` options replace the default cosine sweep.

The exporter:

- includes only active human assignments with the exact requested detector and embedder outputs;
- uses assigned people absent from the gallery as human-confirmed unknown examples;
- assigns each immutable source revision wholly to gallery, validation or test;
- uses SHA-256 ordering from the recorded seed rather than runtime random shuffling;
- records model hashes, pipeline version, source revision IDs and hashes, split settings and a canonical catalogue-input digest;
- never serializes source roots or crop storage paths; and
- fails clearly when the reviewed catalogue cannot support the requested known and unknown split sizes.

If processing-job timing is unavailable for an explicitly selected revision, affected samples receive a deterministic 1 ms fallback and the manifest records the fallback count. Do not use fallback-based throughput as a performance measurement.

## Evaluate a manifest

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset C:\PhotoIdentity\private-evaluation\baseline.json `
  --output C:\PhotoIdentity\private-evaluation\baseline-report.json `
  --archive-images 100000 `
  --hourly-cost 1.50 `
  --currency GBP
```

Fixed input produces byte-for-byte identical manifest and report JSON. The evaluation report contains an SHA-256 digest of the complete input manifest rather than local file paths.

## Dataset schema

Top-level fields:

- `schemaVersion`: currently `1`;
- `datasetId`: stable operator-defined dataset identifier;
- `pipelineVersion`: version for decoding, detection, alignment and embedding policy;
- `detector`: exact detector model ID and SHA-256 hash;
- `embedder`: exact embedding model ID, SHA-256 hash and dimensions;
- `thresholds`: unique cosine thresholds from `-1` through `1`;
- `gallery`: confirmed exemplars with canonical face and source-revision IDs, person ID and embedding;
- `validation`: known and unknown samples used to choose the threshold;
- `test`: known and unknown held-out samples used for final metrics; and
- `catalogueExport`: local export scope, seed, policies, source revision provenance, split settings and canonical catalogue-input digest.

Each validation or test sample records a stable sample ID, canonical face and source-revision IDs, expected person or unknown state, face expectation and detection outcome, an embedding when available and measured elapsed milliseconds.

## Threshold policy

For each configured threshold, the harness performs an exact cosine scan and scores each gallery person by their best exemplar. The selected threshold maximises the validation split's average of known-person identification recall and unknown rejection rate.

Ties prefer higher identification precision, then higher unknown rejection, then the higher threshold. The test split is evaluated after selection and cannot influence the chosen threshold.

## Reported metrics

The deterministic report includes detector recall, identification precision, known-person recall, unknown rejection, balanced identity score, confusion rows, validation and test sweeps, images per second and optional archive runtime and cost projections.

Threshold selection does not imply automatic acceptance in the product. Suggestions remain review-only.
