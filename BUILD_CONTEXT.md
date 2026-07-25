# Build context

## Current milestone

**M01 — Single-image inference**

## Current work item

**WI-0006 — Implement image decoding**

Status: `in_review`

## Branch and pull request

- Branch: `agent/WI-0006-image-decoding`
- Pull request: [#6 — Implement image decoding](https://github.com/erikwasa/Photo-Identity-Indexer/pull/6)

## Objective

Provide deterministic JPEG and PNG decoding behind the neutral `IImageDecoder` contract, including EXIF orientation, explicit BGR pixel layout, resizing and structured failure handling.

## Relevant files

- `src/PhotoIdentity.Imaging.OpenCv/OpenCvImageDecoder.cs`
- `src/PhotoIdentity.Imaging.OpenCv/ImageDecodingException.cs`
- `src/PhotoIdentity.Imaging.OpenCv/README.md`
- `tests/PhotoIdentity.Recognition.Tests/ImageDecoderTests.cs`
- `Directory.Packages.props`
- `docs/delivery/work-items/WI-0006-image-decoder.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Recognition.Tests/PhotoIdentity.Recognition.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test

- JPEG and PNG signatures are accepted; unsupported signatures are rejected explicitly.
- EXIF-rotated JPEG content decodes into the expected orientation.
- Output is a packed application-owned BGR24 `ImageFrame`.
- Maximum-size options preserve aspect ratio without upscaling.
- Corrupt supported media and unsupported media are distinguished.
- Cancellation is honoured.
- `PhotoIdentity.Core` has no OpenCV dependency.

## Verification

GitHub Actions run `30150743391` passed restore, build, all tests, documentation validation and generated-file checks on Windows with .NET 10.

## Known issues

- The current agent container has no .NET SDK; GitHub Actions performs executable verification.
- The current native runtime selection is Windows-only in the test host. Linux runtime packaging belongs to the later worker-container work.
- HEIC is intentionally not supported by this adapter.

## Next action

Review and merge pull request #6, mark WI-0006 completed with merge evidence, then begin WI-0007 — Implement YuNet detection.
