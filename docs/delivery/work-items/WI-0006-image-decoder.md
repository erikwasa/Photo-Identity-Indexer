---
id: WI-0006
title: Implement image decoding
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0003]
affected_modules: [PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Recognition.Tests]
---

# WI-0006: Implement Image Decoding

## Objective

Implement the image abstraction for JPEG and PNG with orientation normalisation, colour conversion, resizing and explicit unsupported-format results.

## Acceptance criteria

- [x] Rotated fixtures decode into the expected orientation.
- [x] Core contracts expose no OpenCV matrices.
- [x] Cancellation and corrupt-media errors are handled.
- [x] JPEG and PNG are distinguished from unsupported signatures.
- [x] Decoded images use an explicit packed BGR24 layout.
- [x] Maximum-size decoding preserves aspect ratio and never upscales.
- [x] HEIC remains a replaceable future adapter.

## Commands

```powershell
dotnet test tests/PhotoIdentity.Recognition.Tests/PhotoIdentity.Recognition.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Implementation notes

- `OpenCvImageDecoder` implements the neutral `IImageDecoder` port.
- JPEG and PNG are identified by file signature rather than filename extension.
- OpenCV colour decoding applies EXIF orientation and yields BGR channel order.
- Pixel rows are copied into a packed application-owned `ImageFrame`; `Mat` never leaves the adapter.
- Resizing uses an aspect-preserving fit and does not enlarge smaller images.
- Unsupported formats and corrupt supported media use separate structured failure values.
- The managed imaging project references OpenCvSharp; the Windows native runtime is selected by the test host.

## Verification

Pull request [#6](https://github.com/erikwasa/Photo-Identity-Indexer/pull/6) contains the implementation.

GitHub Actions run [30150743391](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30150743391) successfully restored, built, tested, validated the living documentation and verified generated files on Windows with .NET 10.

## Completion notes

- Added JPEG and PNG decoding, EXIF orientation normalisation and packed BGR output.
- Added resizing, cancellation handling and structured unsupported/corrupt errors.
- Added generated JPEG/PNG fixtures, including an injected EXIF orientation segment.
- HEIC support remains outside this adapter and can be introduced behind the same core contract.
