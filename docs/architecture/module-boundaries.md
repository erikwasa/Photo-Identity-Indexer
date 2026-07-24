# Module boundaries

## Planned modules

```text
PhotoIdentity.Core
PhotoIdentity.Persistence.Sqlite
PhotoIdentity.Source.Local
PhotoIdentity.Source.OneDriveSync
PhotoIdentity.Imaging.OpenCv
PhotoIdentity.Recognition.Onnx
PhotoIdentity.Transfer.Bundles
PhotoIdentity.Cli
PhotoIdentity.Worker
PhotoIdentity.Api
PhotoIdentity.Web
```

## Dependency direction

```text
Executables and UI
        │
        ▼
Application/Core
        ▲
        │
Infrastructure adapters
```

## Mandatory rules

- Core references no infrastructure project.
- The OneDrive source is a filesystem adapter.
- Recognition adapters do not know whether execution is local or Azure-hosted.
- Bundle code does not depend on SQLite.
- Persistence does not depend on OpenCV or ONNX Runtime.
- Domain contracts expose no EF Core, OpenCV, ONNX Runtime, Azure SDK or Microsoft Graph types.
- Python tools use neutral bundles and exports.
- The Azure worker cannot access the canonical database directly.

## Key contracts

The design expects application-owned interfaces for asset sources, staging, decoding, detection, alignment, embedding, matching, bundle writing, result import, repositories and artefact storage.

Infrastructure-specific image and tensor objects must be converted at adapter boundaries.
