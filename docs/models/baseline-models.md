# Baseline models

The current local baseline is a pinned YuNet detector plus pinned SFace FP32 embedder running through ONNX Runtime from C#.

## Detector: YuNet FP32

| Property | Value |
|---|---|
| Model ID | `yunet-2023mar-fp32` |
| Role | Face detection |
| Runtime | ONNX Runtime |
| Output used | Bounding box, confidence and five-point landmarks |

YuNet remains the accepted local detector because it is lightweight, CPU-capable, ONNX-compatible and supplies the five landmarks required by the SFace alignment protocol.

Detector output is stored as model-versioned observations attached to stable face occurrences. Changing detector revision must not silently replace human review state.

## Embedder: SFace FP32

| Property | Value |
|---|---|
| Model ID | `sface-2021dec-fp32` |
| Role | Face embedding |
| Runtime | ONNX Runtime |
| Alignment | `sface-five-point-v1` |
| Distance | Cosine similarity |

SFace FP32 is the current default embedder for local processing, suggestion regeneration and evaluation.

The model is pinned by a checked-in manifest and locally installed file hash. Model files remain outside Git.

## Runtime policy

CPU inference is the supported default. A different execution provider or materially different runtime behavior must be evaluated under explicit provenance rather than assumed equivalent.

Processing runs persist selected model IDs. Resume uses the saved selection and cannot silently switch to a different detector or embedder.

## Accepted comparison outcome

The same-corpus comparison against `sface-2021dec-int8` kept source revisions, detector, alignment, people, review history and evaluation split fixed. A manual review of 20 representative faces found both revisions correct in every case, with no material practical identification or review-quality advantage for INT8.

Retain `sface-2021dec-fp32` as the current default. This is the accepted local recommendation, not the final production-model decision. Optional Azure consistency, cost and broader-diversity evidence remain future inputs.

## Governance

A baseline is identified by model ID, exact SHA-256 and preprocessing/alignment contract. Installing another candidate does not replace baseline embeddings or alter people, assignments, rejections or audit history.

See:

- [Multi-model comparison workflow](../operations/multi-model-comparison.md)
- [Candidate models](candidate-models.md)
- [Model manifests and governance](model-governance.md)
- [Recognition and identity matching](../architecture/identity-matching.md)
