# Candidate models

## Current candidate: SFace INT8

The first candidate is the upstream INT8-quantised revision of the same December 2021 SFace embedder used by the baseline. This deliberately keeps face detection, five-point alignment, input dimensions and output semantics constant while isolating the effect of the quantised embedding model.

The candidate is not assumed to be more accurate. It is included to compare local CPU throughput, model-file size, embedding quality, unknown rejection and review effort against the FP32 baseline on the same immutable corpus and reviewed split.

## Immutable identity

| Property | Value |
| --- | --- |
| Model ID | `sface-2021dec-int8` |
| Role | Face embedding |
| Format/runtime | ONNX / ONNX Runtime |
| Upstream file | `face_recognition_sface_2021dec_int8.onnx` |
| Source revision | `opencv/face_recognition_sface@89e1f6f89ab68a12ab974b5b65162abf464a461f` |
| SHA-256 | `2b0e941e6f16cc048c20aee0c8e31f569118f65d702914540f7bfdc14048d78a` |
| Size | 9,896,933 bytes |
| Input | 112×112 RGB float32; scale 1.0; zero mean |
| Alignment | `sface-five-point-v1` |
| Output | 128-dimensional embedding, adapter-owned L2 normalisation, cosine distance |

The INT8 graph retains the same external float32 tensor contract used by the existing SFace adapter. Quantisation is part of model identity, so candidate and baseline always use different model IDs and hashes.

## Licence and provenance

The manifest records Apache-2.0 for the OpenCV Zoo code and distributed weights, with pinned upstream licence references. The project does not assert a training-dataset licence; consult the upstream SFace paper and repository before training or redistributing derived weights.

Model files remain outside Git. `models/install-models.ps1` verifies the exact byte size and SHA-256 before a model becomes available to a run.

## Install

From the repository root:

```powershell
./models/install-models.ps1 -Id sface-2021dec-int8
```

The FP32 baseline remains the default. Installing or deleting the candidate file does not alter persisted baseline embeddings, people, labels or review actions.

## Run the same source with the candidate

Use a separate output root for clarity while keeping the same catalogue and immutable source root:

```powershell
$candidateOutput = "C:\PhotoIdentityPilot\outputs-sface-int8"

dotnet run --project src/PhotoIdentity.Cli -- `
  batch start `
  --database $db `
  --source $source `
  --output $candidateOutput `
  --embedder-model sface-2021dec-int8
```

The batch configuration persists the selected detector and embedder IDs. Resume therefore uses the exact same model selection automatically:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch resume --database $db --run CANDIDATE_RUN_ID
```

With the default YuNet detector and the same alignment protocol, the catalogue reuses the existing face-occurrence and crop natural keys and inserts the candidate embedding under its own model ID and exact hash. Baseline and candidate embeddings therefore coexist rather than overwrite one another.

## Evaluation boundary

After candidate processing:

1. regenerate suggestions for the candidate model ID and hash;
2. export the same fixed evaluation split using the candidate revision;
3. retain baseline and candidate manifests/reports separately;
4. compare accuracy, unknown rejection, confusion, throughput, storage and review effort in WI-0030; and
5. do not change canonical labels automatically from either model's score.
