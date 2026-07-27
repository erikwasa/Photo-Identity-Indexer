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

Manage gallery, validation and test datasets and report detector recall, identification precision, unknown rejection, confusion, throughput and threshold sweeps.

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

The manifest records the exact detector model ID and SHA-256 hash, embedder model ID and SHA-256 hash, embedding dimensions and pipeline version. Real manifests, embeddings and reports contain sensitive biometric and identity data and must remain outside the repository. `tools/model-lab/example-dataset.json` is synthetic.

## Threshold selection

For each configured cosine threshold, the harness scores every gallery person by their best exemplar. Threshold selection uses validation data only and maximises the mean of:

1. known-person identification recall; and
2. unknown rejection rate.

Ties prefer higher identification precision, then higher unknown rejection, then the higher threshold. The selected validation threshold is applied unchanged to the held-out test split. The report also includes validation and test sweeps for analysis, but test metrics cannot influence selection.

The threshold grid itself must be defined before inspecting held-out test results. The harness prevents split-ID reuse but cannot detect an operator assigning different identifiers to duplicate media.

## Metrics and confusion

Each split reports:

- detector recall over examples where a face is expected;
- identification precision across accepted predictions;
- known-person identification recall;
- unknown rejection count and rate;
- a balanced identity score;
- measured total milliseconds and images per second;
- confusion rows for expected people, unknowns, accepted identities, explicit rejections and detector misses.

Detector misses remain visible as `<missed>` confusion outcomes. Unknown examples that are not accepted count as successfully rejected for the end-to-end unknown-rejection metric.

## Reproducibility and projections

The report contains no generated timestamp or local input path. Fixed manifest bytes produce byte-for-byte identical report JSON. The report records an SHA-256 digest of the complete manifest so an operator can bind metrics to the exact private evaluation input without publishing it.

When `--archive-images` is supplied, the harness projects full-archive runtime from held-out test throughput. Optional `--hourly-cost` and `--currency` values add a compute-cost projection. The default currency is GBP.

## Operator command

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset "C:\PhotoIdentity\model-lab\dataset.json" `
  --output "C:\PhotoIdentity\model-lab\report.json" `
  --archive-images 100000 `
  --hourly-cost 1.50 `
  --currency GBP
```

The checked-in synthetic example can be exercised without personal data:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset tools/model-lab/example-dataset.json `
  --output .artifacts/model-lab/example-report.json
```

## Validation

`EvaluationCommandTests` covers:

- deterministic report bytes across repeated runs;
- validation-only threshold selection when the test split prefers a different threshold;
- detector, embedder and pipeline provenance;
- detector recall, unknown rejection and threshold sweeps;
- throughput-driven archive time and GBP cost projection;
- rejection of sample identifiers reused across splits.

Pull request [#34](https://github.com/erikwasa/Photo-Identity-Indexer/pull/34) contains the implementation, synthetic fixture, operator documentation and integration coverage. GitHub Actions run `30254226939` passed dependency restore, Release build with warnings as errors, all tests, living-document validation, generated-document checks, the published review application smoke path and Windows mixed-media verification.

## Deliberate limitations

- This slice evaluates supplied detections, embeddings and elapsed timings; it does not build private datasets from source images automatically.
- The balanced validation objective is a deterministic baseline, not a production risk policy.
- Automatic label acceptance remains prohibited; thresholds inform review and model comparison only.
- Dataset composition, demographic representativeness and privacy review remain operator responsibilities.
