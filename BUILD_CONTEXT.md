# Build context

## Current milestone

**M01 — Single-image inference**

## Current work item

**WI-0008 — Implement face crops and alignment**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0008-face-alignment`
- Draft pull request: [#14 — Implement face crops and alignment](https://github.com/erikwasa/Photo-Identity-Indexer/pull/14)

## Objective

Create reusable padded review crops and deterministic five-point aligned model inputs with boundary handling, stable crop hashes and an explicit alignment protocol.

## Relevant files

- `src/PhotoIdentity.Imaging.OpenCv/OpenCvFaceCropper.cs`
- `src/PhotoIdentity.Imaging.OpenCv/OpenCvFaceAligner.cs`
- `src/PhotoIdentity.Imaging.OpenCv/README.md`
- `tests/PhotoIdentity.Recognition.Tests/FaceCropAndAlignmentTests.cs`
- `models/manifests/sface-2021dec-fp32.json`
- `docs/delivery/work-items/WI-0008-alignment.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Recognition.Tests/PhotoIdentity.Recognition.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test

- Padded crops expand outwards from a normalised detection and remain inside decoded source bounds.
- Crop pixels are packed independently of source stride padding.
- Crop SHA-256 digests are stable for equivalent pixels across repeated runs.
- `sface-five-point-v1` produces fixed 112×112 `AlignedFace` values.
- Anatomical landmark semantics are reordered explicitly into the OpenCV SFace reference-template order.
- Rotated synthetic fixtures align back to the canonical model input.
- Unsupported protocols and degenerate landmark configurations fail explicitly.
- OpenCV types remain behind the imaging adapter boundary.

## Verification

WI-0007 is complete. Pull requests #12 and #13 merged, their final Windows workflows passed, and the developer confirmed correct YuNet boxes and landmarks on representative private photos.

The WI-0008 branch uses synthetic edge and rotation fixtures so no private or biometric image data is committed. GitHub Actions run `30167129536` passed restore, Release build, all tests, living-document validation, generated-document checks and the Windows mixed-media verifier.

## Known issues

- The alignment implementation currently supports only the manifest-owned `sface-five-point-v1` protocol.
- Constant zero fill is used when the affine transform samples beyond source bounds.
- The current native test runtime is Windows; hosts on other platforms must select their matching OpenCvSharp runtime package.

## Next action

Review pull request #14, inspect the deterministic crop and alignment contract, then merge and mark WI-0008 completed before starting WI-0009.
