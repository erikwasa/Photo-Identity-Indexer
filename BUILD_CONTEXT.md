# Build context

## Current milestone

**M06 — Evaluation harness**

## Current work item

**WI-0017 — Add evaluation harness**

Status: `in_progress`

## Recently completed milestone

**WI-0016 / M05** completed on 2026-07-27 when pull request #33 merged at `50ca5ca422c8a7026120ff303de87b2a52755473`. The exact matcher ranks at most two distinct people from human-confirmed exemplars, preserves rejected pairs and never changes canonical labels.

## Branch and pull request

- Implementation branch: `agent/WI-0017-evaluation-harness`
- Pull request: [#34 — Add reproducible evaluation harness](https://github.com/erikwasa/Photo-Identity-Indexer/pull/34)

## Objective

Create a reproducible model-lab workflow that separates gallery, validation and held-out test data; selects thresholds from validation only; and reports detector recall, identification precision, unknown rejection, confusion, throughput and archive projections with exact model provenance.

## Current slice

Implement a schema-versioned synthetic-safe dataset manifest and a deterministic `photoid evaluate` command. The validation split chooses one cosine threshold under a documented policy. The held-out test split reports final metrics without influencing selection. Fixed manifest bytes must produce byte-for-byte identical JSON.

## Relevant files

- `src/PhotoIdentity.Cli/EvaluationCommand.cs`
- `src/PhotoIdentity.Cli/Program.cs`
- `src/PhotoIdentity.Core/Recognition/EmbeddingVector.cs`
- `tests/PhotoIdentity.Integration.Tests/EvaluationCommandTests.cs`
- `tools/model-lab/README.md`
- `tools/model-lab/example-dataset.json`
- `docs/delivery/work-items/WI-0017-evaluation.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset tools/model-lab/example-dataset.json `
  --output .artifacts/model-lab/example-report.json `
  --archive-images 100000 `
  --hourly-cost 1.50 `
  --currency GBP

dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- Gallery, validation and test identifiers cannot overlap.
- Known validation and test people must exist in the gallery.
- Validation alone determines the selected threshold.
- A test split that prefers another threshold cannot change the selection.
- Reports identify exact detector and embedder hashes plus pipeline version.
- Detector recall, identification precision, known recall, unknown rejection, confusion and threshold sweeps are retained.
- Held-out test throughput can project archive hours and optional GBP-denominated compute cost.
- Repeated runs over identical manifest bytes produce identical report bytes.

## Verification

Pull request #34 contains the deterministic command, split-leakage guards, synthetic fixture, model-lab operating contract and integration tests. GitHub Actions run `30254226939` passed Release build with warnings as errors, all repository tests, living-document validation, generated-document checks, the published review application smoke path and Windows mixed-media verification.

## Deliberate limitations

- The harness evaluates supplied detector outcomes, embeddings and elapsed timings; it does not automatically assemble private datasets from source images.
- Split-ID checks cannot detect duplicate private media given different identifiers.
- The balanced validation objective is a deterministic baseline, not a production risk policy.
- Thresholds inform review and model comparison only; automatic identity acceptance remains prohibited.
- Real manifests, embeddings, identity identifiers and reports are sensitive local data and must not be committed.
- WI-0020/M09 and WI-0025/M14 are the other ready implementation tracks.

## Next action

Review and merge pull request #34. After merge, run the harness on a privacy-reviewed local gallery, validation and test dataset before using its threshold or archive projection for production planning.
