# PhotoIdentity.Core

## Purpose

Defines application-owned domain values and ports shared by the rest of the solution.

## Public areas

- `Identifiers`: strongly typed entity, model and alignment identifiers
- `Geometry`: pixel and normalised points, boxes, landmarks and IoU
- `Imaging`: immutable neutral image buffers
- `Recognition`: embeddings, model descriptors and recognition interfaces
- `Sources`: source discovery, availability and staging interfaces

## Invariants

- Core references no infrastructure project or external package.
- Pixel and normalised geometry are distinct types.
- Embeddings are immutable, finite and non-zero.
- Model descriptors include a model hash and runtime-independent metadata.
- Interfaces expose no EF Core, OpenCV, ONNX Runtime, Azure SDK or Microsoft Graph types.

## Tests

```powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj
```
