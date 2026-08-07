# Candidate models

Candidate models are installed and evaluated alongside the accepted baseline. They do not replace canonical people, assignments, rejections or review history, and they are never promoted from score evidence alone.

## Active detector candidate: CenterFace

The [face detector candidate registry](face-detector-candidate-registry.md) pre-screened alternatives for WI-0037. It records technical fit, licence and provenance blockers, alignment compatibility, screened-out options and the recommended evaluation order.

WI-0037 became active on 2026-08-07 after governed multi-scale YuNet candidates failed the complete M16 gate. CenterFace ONNX is the first runnable detector candidate. The [CenterFace qualification record](centerface-2019-qualification.md) pins the exact upstream artifact, SHA-256, bounded dynamic input policy, decoder contract, landmark mapping and unresolved governance conditions.

The candidate implementation adds a dedicated ONNX Runtime adapter and local-batch detector selection. It is **not yet authorised for the fixed 100-photo comparison** until the Windows build/tests, exact model installation and disposable smoke procedure in the [CenterFace detector runbook](../operations/centerface-detector-runs.md) pass.

The first full candidate is predeclared as `centerface-2019-fp32`, confidence `0.5`, `single-pass`, maximum source long edge `1600` before multiple-of-32 rounding, SFace FP32 and padding `0.25`. Do not use smoke images to tune that configuration.

## Current embedder candidate: SFace INT8

The first governed embedder candidate is the upstream INT8-quantised revision of the December 2021 SFace embedder.

It deliberately keeps the following fixed relative to the FP32 baseline:

- YuNet detector revision;
- five-point alignment protocol;
- 112 × 112 external input contract;
- 128-dimensional embedding output semantics;
- immutable source scope; and
- canonical people and human review history.

This isolates the effect of the quantised embedding model.

## Immutable identity

| Property | Value |
|---|---|
| Model ID | `sface-2021dec-int8` |
| Role | Face embedding |
| Format/runtime | ONNX / ONNX Runtime |
| Upstream file | `face_recognition_sface_2021dec_int8.onnx` |
| Source revision | `opencv/face_recognition_sface@89e1f6f89ab68a12ab974b5b65162abf464a461f` |
| SHA-256 | `2b0e941e6f16cc048c20aee0c8e31f569118f65d702914540f7bfdc14048d78a` |
| Size | 9,896,933 bytes |
| Input | 112 × 112 RGB float32; scale 1.0; zero mean |
| Alignment | `sface-five-point-v1` |
| Output | 128-dimensional embedding, adapter-owned L2 normalisation, cosine distance |

The INT8 graph retains the same external float32 tensor contract used by the SFace adapter. Quantisation is part of model identity, so INT8 and FP32 always use different model IDs and hashes.

## Licence and provenance

The manifest records Apache-2.0 for the OpenCV Zoo code and distributed weights, with pinned upstream licence references. The project does not assert a training-dataset licence; consult the upstream SFace paper and repository before training or redistributing derived weights.

Model files remain outside Git. Installation verifies exact byte size and SHA-256 before the model becomes available.

## Install and verify

```powershell
./models/install-models.ps1 -Id sface-2021dec-int8
./verify-local.ps1
```

Expected success signals:

- the installed file matches the checked-in manifest size and hash;
- the baseline FP32 file remains installed and unchanged; and
- the candidate is listed as a separate exact model revision.

## Processing and coexistence

Use the [multi-model comparison workflow](../operations/multi-model-comparison.md) rather than manually creating a separate candidate catalogue.

The workflow processes the same source and canonical database with the fixed YuNet detector and candidate embedder. Existing detector-derived face occurrence and crop identities are reused where their natural keys match. Candidate embeddings are inserted under the INT8 model ID and exact hash.

Baseline and candidate embeddings and suggestions therefore coexist. Installing, running or removing the candidate does not alter persisted baseline embeddings, people, assignments, rejections or append-only review history.

## Exact-model selection

Suggestion regeneration, browser filtering, collection advisory evidence and evaluation must select `sface-2021dec-int8` together with its exact hash.

Do not:

- apply a FP32 threshold to INT8 scores;
- mix candidate and baseline embeddings in one similarity calculation;
- infer a model revision from a display name alone; or
- promote candidate suggestions to labels automatically.

## Accepted comparison outcome

The accepted private same-corpus comparison held detector, source revisions, alignment, dataset, split and human review state fixed. A manual review of 20 representative faces found both FP32 and INT8 correct in every case, with no material practical identification or review-quality advantage for INT8.

The current recommendation is:

- retain `sface-2021dec-fp32` as the default embedder; and
- keep `sface-2021dec-int8` as a governed candidate for later runtime, Azure-consistency, cost or broader-corpus evidence.

No larger local evaluation is required before the documentation and optional Azure phases. Final production selection remains deferred.

## Adding another candidate

A new candidate requires:

1. a checked-in immutable manifest and licence/provenance review;
2. local installation with size and hash verification;
3. an explicit model ID distinct from existing revisions;
4. processing under fixed comparison scope;
5. deterministic exact-model suggestion and evaluation evidence; and
6. human review and a documented recommendation.

See [Model manifests and governance](model-governance.md), [Recognition and identity matching](../architecture/identity-matching.md) and the [Glossary](../glossary.md).
