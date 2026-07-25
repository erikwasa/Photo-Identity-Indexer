# Build context

## Current milestone

**M01 — Single-image inference**

## Current work item

**WI-0026 — Add local developer verification**

Status: `in_review`

## Branch and pull request

- Branch: `agent/fix-windows-powershell-verifier`
- Pull request: [#11 — Fix Windows PowerShell media verification](https://github.com/erikwasa/Photo-Identity-Indexer/pull/11)

## Objective

Provide one repeatable Windows checkpoint proving that the repository, native OpenCV runtime, pinned model files and real-photo decoding work together before YuNet ONNX inference is introduced.

## Relevant files

- `verify-local.ps1`
- `.github/workflows/build.yml`
- `README.md`
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
  -Image "C:\PrivateVerification\normal.jpg","C:\PrivateVerification\pixel-rotated.jpg","C:\PrivateVerification\sample.png" `
  -UnsupportedImage "C:\PrivateVerification\sample.heic"

./verify-local.ps1 -Configuration Release -SkipModels

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test

- Release restore, build and tests pass on Windows.
- The verifier passes `Configuration` to PowerShell child scripts as a named parameter.
- CI executes the verifier with `-SkipModels`.
- Array-valued PowerShell parameters are supplied once with all values.
- Native stderr does not terminate the verifier before its structured exit code is recorded.
- Native exit code zero remains authoritative when libjpeg emits a recoverable warning.
- Diagnostic matching works in both Windows PowerShell 5.1 and PowerShell 7.
- A corrupt supported image is recorded as `corrupt_media` while later checks continue.
- Successful and failed media checks remain in the aggregate JSON report.
- Living-document registries, links and generated views validate.
- YuNet and SFace model files pass size and SHA-256 verification.
- Real JPEG, PNG and EXIF-rotated Pixel photos produce upright, viewable PNGs.
- Input hashes are unchanged, including failed decode cases.
- Unsupported HEIC or other media returns exit code 3.
- Reports below `.artifacts/local-verification` contain no source paths or image data.

## Verification

PR #8 passed the full Windows workflow, including the local-verifier smoke step.

PR #9 corrected the private-image invocation examples so PowerShell binds all image paths to the single `string[]` parameter.

PR #10 retained mixed-media results and input hashes after failed decodes.

PR #11 must pass the mixed-media verifier under Windows PowerShell, including a JPEG that emits `Invalid SOS parameters for sequential JPEG` while still decoding successfully.

Human completion requires rerunning the private-image command after PR #11 is merged and manually confirming that all generated PNGs are upright and viewable.

## Known issues

- Some JPEG files can produce recoverable libjpeg warnings on stderr while decoding successfully; native exit code zero is authoritative.
- PowerShell does not permit the same named parameter to be specified more than once, even when its type is an array.
- HEIC is intentionally unsupported by the current decoder and is used only to verify explicit unsupported-format handling.
- The local report is ignored by Git and must not be attached to the PR when it contains information derived from private photos.
- YuNet work remains blocked by WI-0026 until the local private-image checks pass.

## Next action

Merge PR #11 after CI passes, pull `main`, rerun the private-image verification, inspect all generated PNGs, record human evidence, complete WI-0026, then begin WI-0007 — Implement YuNet detection.
