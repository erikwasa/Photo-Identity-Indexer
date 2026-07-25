# Build context

## Current milestone

**M01 — Single-image inference**

## Current work item

**WI-0009 — Implement SFace embeddings**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0009-sface-embeddings`
- Draft pull request: [#15 — Implement SFace embeddings](https://github.com/erikwasa/Photo-Identity-Indexer/pull/15)

## Objective

Add the pinned SFace ONNX embedder behind the neutral `IFaceEmbedder` contract with manifest-driven preprocessing, strict output validation, L2 normalisation, cosine comparison and repeatability evidence.

## Relevant files

- `src/PhotoIdentity.Recognition.Onnx/SFace/OnnxSFaceInferenceSession.cs`
- `src/PhotoIdentity.Recognition.Onnx/SFace/SFaceFaceEmbedder.cs`
- `src/PhotoIdentity.Recognition.Onnx/README.md`
- `tests/PhotoIdentity.Recognition.Tests/SFaceEmbedderTests.cs`
- `models/manifests/sface-2021dec-fp32.json`
- `docs/delivery/work-items/WI-0009-sface.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
./models/install-models.ps1 -Id sface-2021dec-fp32
dotnet test tests/PhotoIdentity.Recognition.Tests/PhotoIdentity.Recognition.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test

- The adapter accepts only `sface-five-point-v1` aligned 112×112 image frames.
- Manifest preprocessing converts application-owned pixels to RGB channel-first float32 tensors without scale or mean subtraction.
- The ONNX model must expose exactly one input and one float32 output.
- Output shape is `[128]` or `[1,128]`, and every component must be finite and non-zero as a vector.
- Returned embeddings are L2-normalised and expose the SFace model descriptor.
- Synthetic same-person fixtures score above selected different-person fixtures.
- Repeated deterministic inference produces equivalent vectors within tolerance.
- Final completion still requires the installed model on private same-person and different-person photos, plus repeated CPU inference of one aligned crop.

## Verification

WI-0008 is complete. Pull request #14 merged at `65b6ffc28212c403b2f98df6bc8cdef70fa3d492`, and GitHub Actions run `30167234799` passed the final Windows workflow.

The SFace preprocessing metadata follows OpenCV `FaceRecognizerSF::feature`, which converts aligned BGR images to RGB float32 without scaling or mean subtraction. Adapter-owned L2 normalisation is applied after inference.

## Known issues

- The current execution provider is CPU-only.
- Real-model similarity and repeatability checks require locally installed model binaries and private photos that must not be committed.
- SFace completion thresholds will be recorded from selected local fixtures rather than treated as universal identity-matching policy.

## Next action

Resolve CI findings on pull request #15, then run the pinned model locally on selected private same-person and different-person photos and record the privacy-safe scores and repeatability tolerance.
