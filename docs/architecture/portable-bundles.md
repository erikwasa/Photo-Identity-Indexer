# Portable processing bundles

Portable bundles separate canonical local identity data from disposable processing compute. They can be used on another local machine or on temporary Azure compute without granting access to personal OneDrive or the canonical SQLite catalogue.

## Trust boundary

The trusted Windows control plane:

- selects immutable asset revisions and requested processing steps;
- pins exact detector/embedder manifests;
- creates checksummed neutral input records;
- retains people, assignments, rejections and append-only review history; and
- validates result provenance before import.

The worker receives only the explicit bundle. It has no OneDrive credentials, canonical database connection, people or human review history.

## Job bundle

```text
job-bundle/
├── bundle-manifest.json
├── pipeline-config.json
├── assets.ndjson
├── model-manifests/
├── input/
└── checksums.sha256
```

Job records use internal revision identifiers and neutral bundle-relative names. They include media/provenance metadata and requested processing operations without requiring original OneDrive or local source paths.

The bundle pins exact model IDs, hashes and preprocessing contracts. A worker cannot substitute another revision silently.

## Result bundle

```text
result-bundle/
├── result-manifest.json
├── assets.ndjson
├── detections.ndjson
├── crops/
├── embeddings/
├── errors.ndjson
├── timings.ndjson
├── checkpoints/
└── checksums.sha256
```

Results are derived data. They identify the input revision and exact model provenance that produced each observation or embedding.

## Worker behavior

The headless worker:

1. verifies bundle checksums and schema;
2. verifies required local model files against bundled manifests;
3. processes only declared inputs;
4. records bounded errors and checkpoints;
5. writes deterministic neutral records where required; and
6. creates a checksummed result bundle.

The worker never makes canonical identity decisions.

## Import rules

The Windows importer:

- verifies result checksums and manifests;
- matches only known immutable revision IDs;
- rejects stale, unknown or conflicting inputs;
- validates exact model and pipeline provenance;
- imports idempotently where supported;
- preserves people, assignments, rejections and review history; and
- records partial, failed or corrupt results explicitly.

A result bundle is not an alternative catalogue backup.

## Privacy profiles

Bundle content can be reduced according to the processing purpose:

- **full-image** for detector evaluation and small faces;
- **reduced-image** for throughput or prominent-face work; and
- **face-crop** for embedding comparison and lower-transfer reprocessing.

Choose the least revealing profile that still supports the accepted task. All profiles remain sensitive.

## Optional Azure scale-out

Azure is disposable compute, not a control plane. It receives explicit job bundles, uses no managed identity or service principal for OneDrive, and returns result bundles for local validation and import.

Deleting the Azure resource must not remove canonical state. The Windows catalogue and review history remain sufficient to recreate later bundles.

## Retention

Delete temporary transfer archives and remote copies after validated import and required diagnostics. Keep private bundles outside Git and protect them as biometric processing data.

See [Architecture overview](overview.md), [Module boundaries](module-boundaries.md), [Canonical data model](data-model.md) and the [Glossary](../glossary.md).
