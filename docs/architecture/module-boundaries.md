# Module boundaries

The implementation is a modular monolith with explicit project boundaries. Executables compose application services and infrastructure adapters; canonical domain contracts remain free of infrastructure-specific types.

## Implemented projects

| Project | Responsibility |
|---|---|
| `PhotoIdentity.Core` | Stable identifiers, model-provenance types and application/domain contracts |
| `PhotoIdentity.Persistence.Sqlite` | Canonical catalogue, review history, processing state and collection queries |
| `PhotoIdentity.Source.Local` | Local filesystem discovery and source access |
| `PhotoIdentity.Source.OneDriveSync` | OneDrive-synchronised-folder availability and staging through the Windows filesystem |
| `PhotoIdentity.Imaging.OpenCv` | Image decoding, crop/alignment support and bounded thumbnail rendering |
| `PhotoIdentity.Recognition.Onnx` | YuNet/SFace ONNX Runtime adapters and model preprocessing |
| `PhotoIdentity.Transfer.Bundles` | Neutral job/result bundle contracts, checksums and validated import/export |
| `PhotoIdentity.Cli` | Local operational orchestration and repeatable commands |
| `PhotoIdentity.Worker` | Headless portable-bundle processing |
| `PhotoIdentity.Api` | Trusted local HTTP API and hosted web application |
| `PhotoIdentity.Web` | Responsive Blazor review and collection UI plus shared HTTP contracts |

Verification and governance tools live under `tools` and tests under `tests`; they may depend on public application contracts but do not become runtime owners of canonical data.

## Dependency direction

```text
Executable composition roots
PhotoIdentity.Cli / Worker / Api / Web
                │
                ▼
       application and core contracts
                ▲
                │
       infrastructure adapters
Persistence / Source / Imaging / Recognition / Bundles
```

Infrastructure adapters can depend on core contracts. `PhotoIdentity.Core` must not reference infrastructure projects.

## Mandatory rules

- Core and shared contracts expose no SQLite, OpenCV, ONNX Runtime, Azure SDK or Microsoft Graph types.
- Personal OneDrive access is a local filesystem concern through the Windows synchronisation client.
- Persistence does not depend on OpenCV or ONNX Runtime.
- Recognition adapters do not know whether orchestration is local or Azure-hosted.
- Model-specific preprocessing stays beside the corresponding recognition adapter and manifest.
- Bundle contracts do not depend on SQLite and do not contain people or human review history.
- The worker never accesses the canonical catalogue or OneDrive credentials directly.
- The API does not perform long-running batch inference inside HTTP requests.
- Python tools exchange documented neutral files rather than importing canonical database internals.
- Original photos are read-only inputs.

## Data crossing module boundaries

Use stable identifiers and neutral records when data crosses projects:

- source and asset identifiers rather than filesystem implementation objects;
- immutable revision identifiers rather than mutable file assumptions;
- bounding boxes, landmarks and embeddings in application-owned representations;
- model ID plus exact hash rather than an unversioned model name;
- opaque revision-based URLs rather than source paths in browser contracts; and
- checksummed bundle records rather than direct remote database access.

OpenCV image matrices and ONNX tensors are adapter-owned and must not escape into core or persistence contracts.

## Canonical and derived ownership

`PhotoIdentity.Persistence.Sqlite` owns durable catalogue and review state. Imaging and recognition projects produce derived observations, crops and embeddings under exact provenance. Transfer code packages selected inputs or results but does not become an alternative source of truth.

The Web project owns shared HTTP response contracts used by the hosted client, but filesystem paths and persistence implementation details remain server-side.

## Composition roots

Each executable selects and wires the adapters it needs:

- the CLI composes local sources, SQLite, imaging, recognition, evaluation and bundle operations;
- the worker composes bundle input/output, imaging and recognition without SQLite identity state;
- the API composes SQLite-backed review/collection services, local file resolution and hosted Web assets; and
- the Web client consumes API contracts only.

See [Applications](applications.md), [Canonical data model](data-model.md) and [Portable processing bundles](portable-bundles.md).
