---
id: WI-0019
title: Add a second model adapter
milestone: M08
status_source: ../status/work-items.yaml
depends_on: [WI-0017, WI-0018, WI-0029, WI-0033]
affected_modules: [PhotoIdentity.Recognition.Onnx, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Worker, PhotoIdentity.Cli, tools/model-lab]
---

# WI-0019: Add a second model adapter

## Objective

Integrate at least one additional detector or embedder through the neutral model contracts after the baseline 500-image workflow is proven.

## Selected candidate

The first candidate is `sface-2021dec-int8`, the upstream INT8-quantised December 2021 SFace embedder. It keeps the baseline detector, 112×112 input, five-point alignment, 128-dimensional output and cosine comparison contract constant. This isolates the effect of quantised embedding weights for the multi-model comparison rather than changing detection and embedding simultaneously.

The FP32 baseline remains the default. Candidate and baseline use distinct model IDs and exact hashes.

## Acceptance criteria

- [x] Existing people, labels and review actions remain unchanged.
- [x] Baseline and candidate embeddings coexist by model ID and exact model hash.
- [x] The same immutable source revisions can be processed by both models without overwriting results.
- [x] The same evaluation set can be exported for both models.
- [x] Licence, source, input contract, dimensions and model hashes are documented.
- [x] Failure or removal of the candidate adapter does not make baseline results unreadable.

## Implemented slice: selectable SFace revisions

- `LocalBatchConfiguration` persists explicit detector and embedder model IDs with backward-compatible baseline defaults for older saved runs.
- `batch start` accepts `--detector-model` and `--embedder-model`; `batch resume` reloads the immutable selection from the saved run rather than accepting a replacement model.
- The production handler resolves the exact manifest by model ID and validates the required detector/embedder role before opening model files.
- The pinned `sface-2021dec-int8` manifest records source revision, byte size, SHA-256, float32 external input/output contract, alignment, dimensions, distance metric and licence records.
- The existing SFace adapter executes either FP32 or INT8 graph because both expose the same neutral face-embedding contract.
- SQLite natural keys reuse the same occurrence and crop for an unchanged detector/alignment result while inserting each embedding under `(face_crop_id, model_id, model_hash)`.
- Integration coverage processes one immutable revision with baseline and candidate embedders and proves one occurrence, one detector observation, one crop, two exact-model embeddings, one confirmed label and one review action.
- The test then reads both persisted embeddings after the candidate handler is disposed, demonstrating that candidate availability is not required to read baseline data.
- The existing exact-model evaluation exporter accepts either embedder ID/hash and the runbook uses the same dataset ID and fixed split seed for both revisions.

## Governance

See [candidate models](../../models/candidate-models.md) for immutable identity, installation, licence notes and local commands. Model binaries remain ignored and must pass byte-size and SHA-256 verification before use.

Quantisation is a material model change. Candidate scores and thresholds must never be mixed with the FP32 baseline, and neither revision may create canonical labels automatically.

## Remaining verification gate

After this slice merges:

1. install `sface-2021dec-int8` with the checked-in model installer;
2. process at least one immutable local revision into a disposable or backed-up catalogue using `--embedder-model sface-2021dec-int8`;
3. confirm the candidate run reports the exact model ID/hash and baseline review data remains readable; and
4. retain only privacy-safe success/failure evidence.

The full 500-image baseline-versus-candidate measurement belongs to WI-0030.
