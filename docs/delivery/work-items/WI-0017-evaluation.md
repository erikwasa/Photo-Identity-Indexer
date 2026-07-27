---
id: WI-0017
title: Add evaluation harness
milestone: M06
status_source: ../status/work-items.yaml
depends_on: [WI-0016]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Core, PhotoIdentity.Integration.Tests, tools/model-lab]
---

# WI-0017: Add evaluation harness

## Objective

Manage gallery, validation and held-out test datasets and report detector recall, identification precision, unknown rejection, confusion, throughput and threshold sweeps.

## Acceptance criteria

- [x] Repeated runs with fixed inputs are reproducible.
- [x] Test data is not used to choose thresholds.
- [x] Reports identify model hashes and pipeline versions.
- [x] Archive runtime and cost can be projected from measured throughput.

## Dataset contract

`photoid evaluate` reads a schema-version-1 JSON manifest with explicit `gallery`, `validation` and `test` splits.

- Gallery rows contain human-confirmed person exemplars.
- Validation rows are the only rows used to select the identity threshold.
- Test rows are held out until selection is complete and are used only for final reporting.
- Gallery face IDs and validation/test sample IDs must be globally unique.
- Every known validation or test identity must exist in the gallery.
- Validation and test each contain at least one known and one unknown example.

The manifest records the exact detector model ID and SHA-256 hash, embedder model ID and SHA-256 hash, embedding dimensions and pipeline version. Real manifests, embeddings and reports contain sensitive biometric and identity data and must remain outside the repository.

## Threshold selection

For each configured cosine threshold, the harness scores every gallery person by their best exemplar. Validation maximises the mean of known-person identification recall and unknown rejection rate. Ties prefer higher identification precision, then higher unknown rejection, then the higher threshold.

The selected threshold is applied unchanged to the held-out test split. Test sweeps are retained for reporting but cannot influence selection.

## Metrics and reproducibility

Each split reports detector recall, accepted-prediction precision, known-person recall, unknown rejection, a balanced identity score, confusion rows, elapsed milliseconds and images per second.

The report contains no generated timestamp or local input path. Fixed manifest bytes produce byte-for-byte identical JSON and the report records the SHA-256 digest of the complete input.

Optional `--archive-images`, `--hourly-cost` and `--currency` values project full-archive runtime and compute cost from held-out throughput.

## Completion

Pull request [#34](https://github.com/erikwasa/Photo-Identity-Indexer/pull/34) merged at `d0093e1a817dd81c905cc3edf908f35e8fe4b65f` on 2026-07-27. GitHub Actions run `30254835059` passed Release build with warnings as errors, all tests, living-document validation, generated-document checks, published review smoke verification and Windows mixed-media verification.

## Follow-on gap

The harness intentionally consumes a prepared manifest. [WI-0028](WI-0028-catalogue-evaluation-export.md) adds deterministic export from a reviewed SQLite catalogue so the 500-image pilot does not require manual embedding or identifier assembly.

## Deliberate limitations

- This slice evaluates supplied detections, embeddings and elapsed timings; it does not build private datasets from source images automatically.
- The balanced validation objective is a deterministic baseline, not a production risk policy.
- Automatic label acceptance remains prohibited; thresholds inform review and model comparison only.
- Dataset composition, demographic representativeness and privacy review remain operator responsibilities.
