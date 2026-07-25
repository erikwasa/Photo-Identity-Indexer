---
id: WI-0010
title: Build photoid inspect command
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0009]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Integration.Tests]
---

# WI-0010: Build `photoid inspect`

## Objective

Compose decoding, detection, cropping, alignment and embedding for one image and write annotated output, crops, embeddings, manifest and timings.

## Acceptance criteria

- [x] Command supports the Windows CPU path for JPEG and PNG through the existing decoder and native runtime.
- [x] Output contains an annotated image, padded crops, aligned model inputs, embeddings, model metadata, hashes and timings.
- [x] Usage, unsupported media, corrupt media, missing models and inference failures have distinct messages and exit codes.
- [x] The source SHA-256 is checked before and after processing and output-directory safety prevents overwrite deletion of the source.

## Command

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  inspect "C:\PrivateVerification\family-photo.jpg" `
  --output ".artifacts\inspect\family-photo" `
  --overwrite `
  --verbose
```

The default output directory is `.artifacts/inspect/<source-name>`. Use `--model-dir` for models installed outside `models/files`, and `--root` when the command is not launched below the repository.

## Output layout

```text
manifest.json
timings.json
normalised.png
annotated.svg
faces/
  face-001/
    crop.png
    aligned.png
    embedding.json
```

`annotated.svg` embeds the normalised source PNG, so it remains viewable if the output directory is moved. `manifest.json` records the source and model hashes, preprocessing metadata, normalised detections and landmarks, crop/alignment metadata, embedding dimensions and hashes for deterministic outputs. Timing values remain in the separate `timings.json` file.

## Exit codes

- `0`: inspection completed and the source remained unchanged;
- `1`: an unexpected failure or source-integrity failure;
- `2`: invalid arguments, missing input or unsafe/non-empty output directory;
- `3`: unsupported media format;
- `4`: corrupt JPEG or PNG;
- `5`: repository, manifest or installed-model data unavailable;
- `6`: ONNX inference or model-output validation failure;
- `130`: cancellation.

## Automated verification

The integration test uses a synthetic PNG plus fake detector/embedder adapters while retaining the real decoder, PNG encoder, cropper and five-point aligner. It verifies the complete output layout, manifest contents, 128-dimensional unit-normalised embedding serialization, embedded annotation, stable non-timing output hashes across repeated runs and unchanged source bytes.

## Deferred M01 verification

After pull request #16 is merged, run this command with installed YuNet and SFace models on representative private JPEG and PNG images. The milestone check will cover visual boxes, landmarks and crops; same-person versus different-person cosine scores; repeated CPU inference stability; and source integrity without committing any biometric output.