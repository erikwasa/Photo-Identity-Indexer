# Photo Identity Indexer

Photo Identity Indexer is a private, local-first system for detecting, reviewing and finding people in a personal photo archive.

The maintainer-controlled Windows computer is the trusted application/control environment. It runs the application and model processing, accesses Personal OneDrive through the Windows sync client, and keeps personal photos and derived biometric data under local control. PostgreSQL running on the maintainer's hardware is the sole writable production catalogue.

The current strategy is local production execution. Historical portable-bundle/Azure material remains in the repository as reference, but Azure is not a planned processing target; see [ADR-0010](docs/decisions/ADR-0010-local-production-execution.md).

## Start here

Follow the [local operator guide](docs/operations/local-operator-guide.md) for the normal packaged/local operating workflow.

Use these references when you need more detail:

- [Documentation index](docs/index.md)
- [Build context](BUILD_CONTEXT.md) for the immediate development/verification handoff
- [Architecture overview](docs/architecture/overview.md)
- [PostgreSQL operations](docs/operations/postgresql-operations.md)
- [Local evaluation and multi-model workflow](docs/operations/local-evaluation.md)
- [Security and privacy](docs/architecture/security-and-privacy.md)
- [Delivery roadmap](docs/delivery/roadmap.md)

## Development status

`BUILD_CONTEXT.md` contains only the current focus and next concrete step. Formal work-item lifecycle status, dependencies and completion evidence are maintained through the canonical work-item shards under [`docs/delivery/status/work-items/`](docs/delivery/status/work-items/).

## Prerequisites

- Windows
- .NET 10 SDK for development/source verification
- PowerShell 7 or Windows PowerShell
- Podman/WSL2 PostgreSQL runtime for the accepted production catalogue
- Local disk space for PostgreSQL, derivatives, analysis output, packages and backups
- A trusted private network for supported phone/browser access
- Personal photos and generated biometric data kept outside the repository

## Verify the repository

From the repository root:

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./verify-review.ps1 -Mode Smoke -Configuration Release
./verify-postgres.ps1
```

The living documentation gate is also expected to pass:

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Supported production workflow

The accepted production workflow is:

1. configure the Personal OneDrive-synchronised archive outside the repository;
2. synchronize included archive coverage through the local application;
3. process immutable revisions with the governed detector and SFace embedder using bounded original hydration;
4. review faces and maintain people through the local browser application;
5. regenerate or apply governed identity suggestions;
6. browse Smart Collections, photo details and slideshow surfaces from the same production catalogue;
7. continue with small incremental archive updates rather than rebuilding the catalogue; and
8. back up and verify the PostgreSQL catalogue using the documented stopped-application procedure.

The [local operator guide](docs/operations/local-operator-guide.md) is the authoritative normal operating path. Other documents explain individual subsystems or retained historical experiments.

## Privacy boundary

Do not commit personal photos, names, face crops, embeddings, catalogues, model binaries, credentials, tokens, real evaluation manifests, reports or private paths.

The browser application is unauthenticated. Bind it only to localhost or a trusted private network, restrict any firewall rule to the intended private profile, and never expose it to the public internet.

Original photos are read-only inputs and must not be modified.
