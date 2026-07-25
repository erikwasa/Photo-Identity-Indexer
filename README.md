# Photo Identity Indexer

A private, model-independent system for detecting and identifying people in a personal photo archive.

The project is a local-first modular .NET application. Personal OneDrive is accessed through the Windows sync client. Optional Azure compute receives explicit portable job bundles and does not authenticate to OneDrive or use Azure application identities.

## Project status

The project is currently in **M01 — Single-image inference**. WI-0026 is complete, and WI-0007 is implementing YuNet face detection behind the neutral recognition contracts.

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

## First target demonstration

1. Run `photoid inspect family-photo.jpg` after the remaining M01 inference work is complete.
2. Verify detected face boxes and crops.
3. Generate SFace embeddings.
4. Compare same-person and different-person similarities.

## Privacy

Do not commit personal photos, face crops, embeddings, model binaries, credentials, SAS tokens, or generated biometric data. See [security and privacy](docs/architecture/security-and-privacy.md).
