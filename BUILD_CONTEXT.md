# Build context

## Current milestone

**M01 — Single-image inference**

## Current work item

**WI-0026 — Add local developer verification**

Status: `in_review`

## Branch and pull request

- Branch: `agent/fix-local-verification-arguments`
- Pull request: [#8 — Fix local verification PowerShell argument binding](https://github.com/erikwasa/Photo-Identity-Indexer/pull/8)

## Objective

Provide one repeatable Windows checkpoint proving that the repository, native OpenCV runtime, pinned model files and real-photo decoding work together before YuNet ONNX inference is introduced.

## Relevant files

- `verify-local.ps1`
- `.github/workflows/build.yml`
- `src/PhotoIdentity.Cli/Program.cs`
- `src/PhotoIdentity.Cli/PhotoIdentity.Cli.csproj`
- `src/PhotoIdentity.Imaging.OpenCv/OpenCvPngEncoder.cs`
- `tests/PhotoIdentity.Integration.Tests/DecodeCommandTests.cs`
- `docs/delivery/work-items/WI-0026-local-verification.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
./verify-local.ps1 -InstallModels

./verify-local.ps1 `
  -Image "C:\PrivateVerification\normal.jpg" `
  -Image "C:\PrivateVerification\pixel-rotated.jpg" `
  -Image "C:\PrivateVerification\sample.png" `
  -UnsupportedImage "C:\PrivateVerification\sample.heic"

./verify-local.ps1 -Configuration Release -SkipModels

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test

- Release restore, build and tests pass on Windows.
- The verifier passes `Configuration` to PowerShell child scripts as a named parameter.
- CI executes the verifier with `-SkipModels`.
- Living-document registries, links and generated views validate.
- YuNet and SFace model files pass size and SHA-256 verification.
- Real JPEG, PNG and EXIF-rotated Pixel photos produce upright, viewable PNGs.
- Input hashes are unchanged.
- Unsupported HEIC or other media returns exit code 3.
- Reports below `.artifacts/local-verification` contain no source paths or image data.

## Verification

PR #8 must pass the full Windows workflow, including the local-verifier smoke step.

Human completion requires rerunning `./verify-local.ps1 -InstallModels`, then running the private-image command and inspecting the generated PNGs.

## Known issues

- PR #7 used array splatting for PowerShell script arguments. That caused `-Configuration` to bind as the parameter value instead of the parameter name.
- HEIC is intentionally unsupported by the current decoder and is used only to verify explicit unsupported-format handling.
- The local report is ignored by Git and must not be attached to the PR when it contains information derived from private photos.
- YuNet work remains blocked by WI-0026 until the local private-image checks pass.

## Next action

Merge PR #8 after CI passes, rerun the model and private-image verification locally, record human evidence, complete WI-0026, then begin WI-0007 — Implement YuNet detection.
