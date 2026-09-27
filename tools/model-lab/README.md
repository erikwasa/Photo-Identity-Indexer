# Model lab evaluation datasets

The model lab uses a versioned JSON manifest with three explicit identity-evaluation splits:

- `gallery` contains human-confirmed exemplars used for matching;
- `validation` is the only split used to select an identity threshold;
- `test` is held out until the threshold has been selected and is used only for final reporting.

Do not reuse a face or source revision across splits. Personal images, crops, embeddings, identity identifiers, real manifests and reports remain local and must not be committed. The checked-in example is synthetic.

## Evaluation manifests

The former catalogue-backed `evaluate export` command was retired with the SQLite migration surface under WI-0149. Existing private manifests remain valid inputs to the provider-neutral evaluator. Any new catalogue export must be introduced as a separately scoped PostgreSQL-backed workflow with the same privacy, exact-model provenance and deterministic split guarantees; do not restore the retired command or create a second catalogue.

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
