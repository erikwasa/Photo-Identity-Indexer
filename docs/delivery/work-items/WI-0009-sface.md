---
id: WI-0009
title: Implement SFace embeddings
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0005, WI-0008]
affected_modules: [PhotoIdentity.Recognition.Onnx, PhotoIdentity.Recognition.Tests]
---

# WI-0009: Implement SFace embeddings

## Objective

Add the SFace ONNX embedder with documented preprocessing, output validation, L2 normalisation and cosine similarity tests.

## Acceptance criteria

- [x] Embeddings contain finite values and expected dimensions.
- [x] Normalised vector norms meet tolerance.
- [ ] Same-person private-photo fixtures score above selected different-person fixtures with the installed model.
- [ ] Repeated real-model CPU inference is stable within tolerance.

## Implemented surface

- `SFaceFaceEmbedder` implements the neutral `IFaceEmbedder` contract and exposes optional stage timings.
- `OnnxSFaceInferenceSession` requires exactly one input and one float32 output and owns all ONNX Runtime buffers.
- The adapter requires the manifest-owned `sface-five-point-v1` protocol and fixed 112×112 aligned inputs.
- Preprocessing creates RGB channel-first float32 tensors from application-owned image frames.
- The manifest now matches OpenCV `FaceRecognizerSF::feature`: RGB conversion without scale or mean subtraction.
- Output shapes are restricted to `[128]` or `[1,128]`, and all components must be finite and non-zero.
- Raw output is L2-normalised before returning an `EmbeddingVector` for persistence or cosine comparison.

## Automated tests

Deterministic tests run without model downloads or biometric fixtures and cover:

- RGB NCHW preprocessing and manifest metadata;
- 128 finite output components and unit L2 norm;
- same-person synthetic fixture similarity above a selected different fixture;
- repeated deterministic pipeline output within tolerance;
- explicit failures for invalid shapes, non-finite values, protocol mismatch and wrong input dimensions.

## Commands

```powershell
./models/install-models.ps1 -Id sface-2021dec-fp32
dotnet test tests/PhotoIdentity.Recognition.Tests/PhotoIdentity.Recognition.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Verification

Draft pull request [#15](https://github.com/erikwasa/Photo-Identity-Indexer/pull/15) contains the implementation.

GitHub Actions run [30168478981](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30168478981) passed restore, Release build, all automated tests, living-document validation, generated-document checks and the Windows mixed-media verifier.

The remaining completion evidence is a local run of the pinned SFace model using selected private same-person and different-person photos, including repeated CPU inference of the same aligned crop. No private photos, crops or embeddings may be committed.
