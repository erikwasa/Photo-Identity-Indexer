# Portable processing bundles

Portable bundles separate canonical local data from disposable compute.

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

Asset records contain internal revision IDs, neutral filenames, media types, hashes, capture dates, orientation data and requested processing steps. They do not require original OneDrive paths.

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

## Import rules

The importer verifies checksums and manifests, matches known revisions, rejects stale assets, imports idempotently, preserves labels and records partial or corrupt results.

## Privacy profiles

- Full-image bundles for detector evaluation and small faces
- Reduced-image bundles for throughput and prominent faces
- Face-crop bundles for embedding comparison and private low-cost reprocessing
