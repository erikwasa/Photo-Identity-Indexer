# Build context

## Current milestone

**M01 — Single-image inference**

## Current work item

**WI-0026 — Add local developer verification**

Status: `in_review`

## Branch and pull request

- Branch: `agent/WI-0026-local-verification`
- Pull request: [#7 — Add local developer verification](https://github.com/erikwasa/Photo-Identity-Indexer/pull/7)

## Objective

Provide one repeatable Windows checkpoint proving that the repository, native OpenCV runtime, pinned model files and real-photo decoding work together before YuNet ONNX inference is introduced.

## Relevant files

- `verify-local.ps1`
- `src/PhotoIdentity.Cli/Program.cs`
- `src/PhotoIdentity.Cli/PhotoIdentity.Cli.csproj`
- `src/PhotoIdentity.Imaging.OpenCv/OpenCvPngEncoder.cs`
- `tests/PhotoIdentity.Integration.Tests/DecodeCommandTests.cs`
- `docs/delivery/work-items/WI-0026-local-verification.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Commands

```powershell
./verify-local.ps1 -InstallModels

./verify-local.ps1 `
  -Image "C:\PrivateVerification\normal.jpg" `
  -Image "C:\PrivateVerification\pixel-rotated.jpg" `
  -Image "C:\PrivateVerification\sample.png" `
  -UnsupportedImage "C:\PrivateVerification\sample.heic"

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test

- Release restore, build and tests pass on Windows.
- Living-document registries, links and generated views validate.
- YuNet and SFace model files pass size and SHA-256 verification.
- Real JPEG, PNG and EXIF-rotated Pixel photos produce upright, viewable PNGs.
- Input hashes are unchanged.
- Unsupported HEIC or other media returns exit code 3.
- Reports below `.artifacts/local-verification` contain no source paths or image data.

## Verification

GitHub Actions must pass the automated repository path on the final PR head.

Human completion requires running the private-image command on the developer's Windows computer and inspecting the generated PNGs.

## Known issues

- HEIC is intentionally unsupported by the current decoder and is used only to verify explicit unsupported-format handling.
- The local report is ignored by Git and must not be attached to the PR when it contains information derived from private photos.
- YuNet work remains blocked by WI-0026 until the local private-image checks pass.

## Next action

Merge pull request #7 after CI passes, run the private-image verification locally, record human evidence, complete WI-0026, then begin WI-0007 — Implement YuNet detection.
