# Photo Identity Indexer

A private, model-independent system for detecting and identifying people in a personal photo archive.

The project is a local-first modular .NET application. Personal OneDrive is accessed through the Windows sync client. Optional Azure compute receives explicit portable job bundles and does not authenticate to OneDrive or use Azure application identities.

## Project status

The project is currently in **M00 — Repository and architecture**. The .NET solution skeleton is under review in WI-0002.

- [Documentation index](docs/index.md)
- [Current build context](BUILD_CONTEXT.md)
- [Roadmap](docs/delivery/roadmap.md)
- [Canonical work-item status](docs/delivery/status/work-items.yaml)

## Prerequisites

- .NET 10 SDK
- PowerShell 7 or Windows PowerShell

## Build and test

```powershell
./build.ps1
./test.ps1
```

## First target demonstration

1. Run `photoid inspect family-photo.jpg`.
2. Verify detected face boxes and crops.
3. Generate SFace embeddings.
4. Compare same-person and different-person similarities.

## Privacy

Do not commit personal photos, face crops, embeddings, model binaries, credentials, SAS tokens, or generated biometric data. See [security and privacy](docs/architecture/security-and-privacy.md).
