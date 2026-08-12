# Photo Identity Indexer

Photo Identity Indexer is a private, local-first system for detecting, reviewing and finding people in a personal photo archive.

The Windows computer is the trusted control plane. It owns the SQLite catalogue, people, append-only human review history and derived artefacts; runs the CLI, worker, API and browser UI; and can perform the complete workflow without Azure. Optional Azure compute is limited to explicit portable processing bundles and never authenticates to personal OneDrive.

## Start here

Follow the [local operator guide](docs/operations/local-operator-guide.md) for the normal packaged/local operating workflow.

Use these references when you need more detail:

- [Documentation index](docs/index.md)
- [Build context](BUILD_CONTEXT.md) for the immediate development/verification handoff
- [Architecture overview](docs/architecture/overview.md)
- [Local evaluation and multi-model workflow](docs/operations/local-evaluation.md)
- [SQLite backup, restore and concurrency policy](docs/operations/sqlite-persistence.md)
- [Security and privacy](docs/architecture/security-and-privacy.md)
- [Delivery roadmap](docs/delivery/roadmap.md)

## Development status

`BUILD_CONTEXT.md` contains only the current focus and next concrete step. Formal work-item lifecycle status, dependencies and completion evidence are maintained in [`docs/delivery/status/work-items.yaml`](docs/delivery/status/work-items.yaml).

## Prerequisites

- Windows
- .NET 10 SDK
- PowerShell 7 or Windows PowerShell
- Local disk space for SQLite, crops, embeddings, publish output and reports
- A trusted private network for optional Pixel browser access
- Personal photos and all generated biometric data kept outside the repository

## Verify the repository

From the repository root:

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./verify-review.ps1 -Mode Smoke -Configuration Release
```

Expected success signals are a Release build, passing automated tests, verified model hashes, valid living documentation and a passing disposable hosted-application smoke test.

## Supported local workflow

The accepted workflow is:

1. stage a local or OneDrive-synchronised source outside the repository;
2. process immutable photo revisions with the governed detector and SFace embedder;
3. review faces and maintain people through the local browser application;
4. regenerate ranked suggestions for one exact embedding-model revision;
5. export and evaluate deterministic reviewed-catalogue splits;
6. optionally process the same revisions with another pinned model revision;
7. browse collections and request neutral manifests; and
8. stop writers before backing up the SQLite catalogue and referenced artefacts.

The [local operator guide](docs/operations/local-operator-guide.md) is the authoritative normal operating path. Other documents explain individual subsystems rather than duplicating that sequence.

## Privacy boundary

Do not commit personal photos, names, face crops, embeddings, SQLite catalogues, model binaries, credentials, tokens, real evaluation manifests, reports or private paths.

The browser application is unauthenticated. Bind it only to localhost or a trusted private network, restrict any firewall rule to the intended private profile, and never expose it to the public internet.

Original photos are read-only inputs and must not be modified.
