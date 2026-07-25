---
id: WI-0026
title: Add local developer verification
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0006]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Integration.Tests, verify-local.ps1]
---

# WI-0026: Add Local Developer Verification

## Objective

Provide one repeatable Windows checkpoint that proves the repository, native OpenCV runtime, verified model files and real-photo decoding work together before ONNX inference is introduced.

## Acceptance criteria

- [x] `verify-local.ps1` restores, builds and tests the Release solution.
- [x] The script validates registries, links and generated living-document views.
- [x] The script can install and verify the pinned YuNet and SFace files.
- [x] `photoid decode` normalises JPEG or PNG content through the production decoder.
- [x] Verification output is written below ignored `.artifacts/local-verification`.
- [x] Reports omit source paths and biometric content.
- [x] The input file hash is checked before and after decoding.
- [x] Unsupported and corrupt media return stable, distinct exit codes.
- [ ] A local run succeeds with a real JPEG, a real PNG and an EXIF-rotated Pixel photo.
- [ ] The generated PNGs are manually inspected and are upright and viewable.
- [ ] A local HEIC or other unsupported file returns the expected unsupported-format result.

## Commands

Automated repository and model verification:

```powershell
./verify-local.ps1 -InstallModels
```

Private-image verification:

```powershell
./verify-local.ps1 `
  -Image "C:\PrivateVerification\normal.jpg" `
  -Image "C:\PrivateVerification\pixel-rotated.jpg" `
  -Image "C:\PrivateVerification\sample.png" `
  -UnsupportedImage "C:\PrivateVerification\sample.heic"
```

Decode one image directly:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  decode `
  --input "C:\PrivateVerification\pixel-rotated.jpg" `
  --output ".artifacts\local-verification\pixel-normalised.png" `
  --report ".artifacts\local-verification\pixel-report.json"
```

## Implementation notes

- The CLI host selects the Windows OpenCV native runtime; the imaging library remains runtime-neutral.
- `photoid decode` is a narrow checkpoint command and does not perform face detection.
- The PNG encoder remains inside the OpenCV adapter, so `Mat` does not cross the module boundary.
- Console output omits the input path unless `--verbose` is used.
- Per-image reports contain dimensions, pixel format, timing, output filename and the input-unchanged result, but not the source path.
- The aggregate verification report is local-only and ignored by Git.
- CI proves the automated path. Completion still requires manual verification on the developer's Windows computer with private images.

## Verification

Pull request [#7](https://github.com/erikwasa/Photo-Identity-Indexer/pull/7) contains the implementation.

The work item remains `in_review` until CI passes and the private-image checks above are completed locally.
