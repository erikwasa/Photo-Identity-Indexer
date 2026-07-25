# Build context

## Current milestone

**M01 — Single-image inference**

## Current work item

**WI-0005 — Add model installation and verification**

Status: `in_review`

## Branch and pull request

- Branch: `agent/WI-0005-model-installation`
- Pull request: [#5 — Add model installation and verification](https://github.com/erikwasa/Photo-Identity-Indexer/pull/5)

## Objective

Provide strict, model-independent manifests and verified installation for the YuNet detector and SFace embedder without committing model binaries.

## Relevant files

- `models/manifests/`
- `models/install-models.ps1`
- `models/README.md`
- `src/PhotoIdentity.Recognition.Onnx/Models/`
- `src/PhotoIdentity.Recognition.Onnx/README.md`
- `tools/PhotoIdentity.Models/`
- `tests/PhotoIdentity.Recognition.Tests/ModelManifestTests.cs`
- `tests/PhotoIdentity.Recognition.Tests/ModelInstallerTests.cs`
- `docs/delivery/work-items/WI-0005-model-installation.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
./models/install-models.ps1
dotnet run --project tools/PhotoIdentity.Models -- list
dotnet run --project tools/PhotoIdentity.Models -- verify
dotnet test tests/PhotoIdentity.Recognition.Tests/PhotoIdentity.Recognition.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test

- Repository manifests load under a strict camel-case JSON contract.
- Unknown properties and incomplete model semantics are rejected.
- Installed files must match both expected size and SHA-256.
- Mismatched downloads are deleted and never promoted.
- Existing valid model files are reused.
- Code, weights and training-data licence considerations are recorded separately.

## Verification

GitHub Actions run `30137094223` passed restore, build, tests, documentation validation and generated-file checks on Windows with .NET 10.

## Known issues

- The current agent container has no .NET SDK; GitHub Actions performs executable verification.
- Model binaries are intentionally not downloaded in CI.
- The training-data entries record upstream provenance but do not assert unrestricted dataset licences.

## Next action

Review and merge pull request #5, then mark WI-0005 completed and begin WI-0006 — Implement image decoding.
