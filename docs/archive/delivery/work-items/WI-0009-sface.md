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
- [x] Deterministic same-source fixtures score above selected different-source fixtures.
- [x] Repeated CPU pipeline inference is stable within tolerance.

## Implemented surface

- `SFaceFaceEmbedder` implements the neutral `IFaceEmbedder` contract and exposes optional stage timings.
- `OnnxSFaceInferenceSession` requires exactly one input and one float32 output and owns all ONNX Runtime buffers.
- The adapter requires the manifest-owned `sface-five-point-v1` protocol and fixed 112×112 aligned inputs.
- Preprocessing creates RGB channel-first float32 tensors from application-owned image frames.
- The manifest matches OpenCV `FaceRecognizerSF::feature`: RGB conversion without scale or mean subtraction.
- Output shapes are restricted to `[128]` or `[1,128]`, and all components must be finite and non-zero.
- Raw output is L2-normalised before returning an `EmbeddingVector` for persistence or cosine comparison.

## Automated tests

Deterministic tests run without model downloads or biometric fixtures and cover:

- RGB NCHW preprocessing and manifest metadata;
- 128 finite output components and unit L2 norm;
- same-source synthetic fixture similarity above a selected different fixture;
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

Pull request [#15](https://github.com/erikwasa/Photo-Identity-Indexer/pull/15) introduced the implementation and merged as commit `19b36537368304f4b7c11bd330f6e6089338eca6`.

GitHub Actions run [30168578069](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30168578069) passed restore, Release build, all automated tests, living-document validation, generated-document checks and the Windows mixed-media verifier.

## Deferred M01 real-model check

The developer requested that installed-model verification be performed once the end-to-end `photoid inspect` command is available. The post-WI-0010 M01 checkpoint will use selected private same-person and different-person photos, compare cosine scores and repeat CPU inference of the same aligned crop. No private photos, crops or embeddings may be committed.