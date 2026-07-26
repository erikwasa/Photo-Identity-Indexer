# Photo Identity Indexer

A private, model-independent system for detecting and identifying people in a personal photo archive.

The project is a local-first modular .NET application. Personal OneDrive is accessed through the Windows sync client. Optional Azure compute receives explicit portable job bundles and does not authenticate to OneDrive or use Azure application identities.

## Project status

The project is currently in **M02 — Local catalogue and jobs**. M01 single-image inference, WI-0011 SQLite persistence and WI-0012 local-folder scanning are complete and verified. WI-0013 now has leased resumable orchestration and is connecting it to production local batch inspection; the remaining acceptance step is a private 500-photo verification.

- [Documentation index](docs/index.md)
- [Current build context](BUILD_CONTEXT.md)
- [Roadmap](docs/delivery/roadmap.md)
- [Canonical work-item status](docs/delivery/status/work-items.yaml)
- [SQLite persistence operations](docs/operations/sqlite-persistence.md)

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

Start a durable local-folder batch. The output directory must be outside the source root:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch start `
  --database "C:\PhotoIdentity\catalogue.db" `
  --source "C:\Photos" `
  --output "C:\PhotoIdentity\outputs"
```

Resume, inspect or cancel the run using the printed run ID:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch resume --database "C:\PhotoIdentity\catalogue.db" --run RUN_ID

dotnet run --project src/PhotoIdentity.Cli -- `
  batch status --database "C:\PhotoIdentity\catalogue.db" --run RUN_ID

dotnet run --project src/PhotoIdentity.Cli -- `
  batch cancel --database "C:\PhotoIdentity\catalogue.db" --run RUN_ID
```

Batch start scans JPEG and PNG files, records unsupported files separately, creates one durable job for each current revision and processes aligned crops and embeddings into SQLite. Resume reconstructs the saved source, output and model configuration.

## First target demonstration

1. Run `photoid inspect family-photo.jpg`.
2. Verify detected face boxes, landmarks, padded crops and aligned model inputs.
3. Confirm 128-dimensional unit-normalised SFace embeddings.
4. Compare same-person and different-person cosine similarities and repeated CPU inference.

## Privacy

Do not commit personal photos, face crops, embeddings, model binaries, credentials, SAS tokens, or generated biometric data. See [security and privacy](docs/architecture/security-and-privacy.md).
