# Photo Identity Indexer

A private, model-independent system for detecting and identifying people in a personal photo archive.

The project is a local-first modular .NET application. Personal OneDrive is accessed through the Windows sync client. Optional Azure compute receives explicit portable job bundles and does not authenticate to OneDrive or use Azure application identities.

## Project status

The project is currently in **M02 — Local catalogue and jobs**. M01 single-image inference is complete and verified; WI-0011 is establishing the versioned SQLite catalogue before repository CRUD, local scanning and resumable processing are added.

- [Documentation index](docs/index.md)
- [Current build context](BUILD_CONTEXT.md)
- [Roadmap](docs/delivery/roadmap.md)
- [Canonical work-item status](docs/delivery/status/work-items.yaml)

## Prerequisites

- .NET 10 SDK
- PowerShell 7 or Windows PowerShell
- Windows for the current OpenCV native-runtime verification path

## Build and test

```powershell
./build.ps1
./test.ps1
```

## Local verification checkpoint

Install and verify the pinned models, build the solution, run all tests and validate the living documentation:

```powershell
./verify-local.ps1 -InstallModels
```

Verify real private images without committing them to the repository. Array-valued parameters such as `-Image` must be supplied once with all values:

```powershell
./verify-local.ps1 `
  -Image "C:\PrivateVerification\normal.jpg","C:\PrivateVerification\pixel-rotated.jpg","C:\PrivateVerification\sample.png" `
  -UnsupportedImage "C:\PrivateVerification\sample.heic"
```

Outputs and privacy-safe reports are written below ignored `.artifacts/local-verification`. Inspect the generated PNGs manually and confirm that the originals remain unchanged.

Decode one JPEG or PNG directly:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  decode `
  --input "C:\PrivateVerification\pixel-rotated.jpg" `
  --output ".artifacts\local-verification\pixel-normalised.png"
```

Run the complete single-image inspection path:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  inspect "C:\PrivateVerification\family-photo.jpg" `
  --output ".artifacts\inspect\family-photo" `
  --overwrite `
  --verbose
```

The inspect output contains an embedded-image annotated SVG, padded and aligned face PNGs, one JSON embedding per face, a reproducibility manifest and stage timings. The command verifies that the original source hash remains unchanged.

## First target demonstration

1. Run `photoid inspect family-photo.jpg`.
2. Verify detected face boxes, landmarks, padded crops and aligned model inputs.
3. Confirm 128-dimensional unit-normalised SFace embeddings.
4. Compare same-person and different-person cosine similarities and repeated CPU inference.

## Privacy

Do not commit personal photos, face crops, embeddings, model binaries, credentials, SAS tokens, or generated biometric data. See [security and privacy](docs/architecture/security-and-privacy.md).
