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
- [x] CI executes the verifier itself with model downloads skipped.
- [x] Native stderr is captured without discarding structured media exit codes.
- [x] Native exit code zero is treated as success when a decoder emits a recoverable warning.
- [x] Diagnostic matching is compatible with Windows PowerShell 5.1 and PowerShell 7.
- [x] One failed image does not prevent later media checks from running.
- [x] Successful and failed cases are retained in the aggregate JSON report.
- [x] A local run succeeds with a real JPEG, a real PNG and an EXIF-rotated Pixel photo.
- [x] The generated PNGs are manually inspected and are upright and viewable.
- [x] A local HEIC or other unsupported file returns the expected unsupported-format result.

## Commands

Automated repository and model verification:

```powershell
./verify-local.ps1 -InstallModels
```

Private-image verification. Supply the array-valued `-Image` parameter once with all image paths:

```powershell
./verify-local.ps1 `
  -Image "C:\PrivateVerification\normal.jpg","C:\PrivateVerification\pixel-rotated.jpg","C:\PrivateVerification\sample.png" `
  -UnsupportedImage "C:\PrivateVerification\sample.heic"
```

An explicit array expression is also valid:

```powershell
./verify-local.ps1 `
  -Image @(
    "C:\PrivateVerification\normal.jpg"
    "C:\PrivateVerification\pixel-rotated.jpg"
    "C:\PrivateVerification\sample.png"
  ) `
  -UnsupportedImage "C:\PrivateVerification\sample.heic"
```

CI-only smoke path without model downloads:

```powershell
./verify-local.ps1 -Configuration Release -SkipModels
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
- PowerShell script parameters are passed through a hashtable; native executable arguments use an array. This avoids positional binding of strings such as `-Configuration`.
- PowerShell named parameters may be specified only once per invocation; multiple image paths must be passed as one array value.
- Native stderr is captured with error promotion disabled around child-process execution.
- Native executable exit code zero is authoritative even if Windows PowerShell sets `$?` to false because stderr contains a warning.
- Ordinal diagnostic matching uses `IndexOf`, which is available in Windows PowerShell 5.1, rather than newer `Contains` overloads.
- Exit code 3 is recorded as `unsupported_format`; exit code 4 is recorded as `corrupt_media`.
- Failed media cases include the input-unchanged result and do not prevent subsequent checks.
- `-SkipModels` exists for CI regression coverage and cannot be combined with `-InstallModels`.
- CI proves the automated path. Completion additionally required manual verification on the developer's Windows computer with private images.

## Verification

Pull request [#7](https://github.com/erikwasa/Photo-Identity-Indexer/pull/7) introduced the local verification harness.

Pull request [#8](https://github.com/erikwasa/Photo-Identity-Indexer/pull/8) fixed PowerShell named-parameter binding and added a CI smoke run of the verifier.

Pull request [#9](https://github.com/erikwasa/Photo-Identity-Indexer/pull/9) corrected the array-valued private-image invocation examples.

Pull request [#10](https://github.com/erikwasa/Photo-Identity-Indexer/pull/10) retained mixed-media results and input hashes after failed decodes.

Pull request [#11](https://github.com/erikwasa/Photo-Identity-Indexer/pull/11) was merged as commit `c7002eca9ebeb8f00e2fcdb96bb6a0e31d252063`. GitHub Actions run [30162808500](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30162808500) passed the final automated Windows verification path.

On 2026-07-25, the developer reran the private-image verification, confirmed the JPEG, PNG and EXIF-rotated Pixel outputs were upright and viewable, and confirmed the unsupported-media result. WI-0026 is complete.
