---
id: WI-0005
title: Add model installation and verification
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0003]
affected_modules: [PhotoIdentity.Recognition.Onnx, PhotoIdentity.Models, models, PhotoIdentity.Recognition.Tests]
---

# WI-0005: Add Model Installation and Verification

## Objective

Define model manifests and provide installation tooling that downloads YuNet and SFace, verifies SHA-256 values and records model provenance and licences.

## Acceptance criteria

- [x] Model files remain ignored by Git.
- [x] A mismatched size or SHA-256 prevents use.
- [x] Manifests describe preprocessing, dimensions, alignment and output semantics.
- [x] Code and weight licences are recorded separately.
- [x] Training-data provenance and unresolved licensing considerations are explicit.
- [x] Downloads are pinned to an upstream repository revision.
- [x] Installation is atomic and does not promote a partial or mismatched file.
- [x] Valid installed models are not downloaded again.
- [x] Unknown manifest properties are rejected.

## Commands

```powershell
./models/install-models.ps1
./models/install-models.ps1 -Id yunet-2023mar-fp32
dotnet run --project tools/PhotoIdentity.Models -- list
dotnet run --project tools/PhotoIdentity.Models -- verify
dotnet test tests/PhotoIdentity.Recognition.Tests/PhotoIdentity.Recognition.Tests.csproj
```

## Implementation notes

- YuNet and SFace are pinned to OpenCV Zoo commit `47534e27c9851bb1128ccc0102f1145e27f23f98`.
- Expected SHA-256 values and file sizes are taken from the upstream Git LFS pointers at that revision.
- Binary files are installed below `models/files` and remain outside Git.
- The manifest JSON contract uses explicit camel-case property names and rejects unmapped members.
- The installer writes to a unique temporary file, verifies it, then atomically promotes it to the final path.

## Verification

Pull request [#5](https://github.com/erikwasa/Photo-Identity-Indexer/pull/5) contains the implementation.

GitHub Actions run [30137094223](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30137094223) successfully restored, built, tested, validated the living documentation and verified generated files on Windows with .NET 10.

## Completion notes

- Added strict manifest, loader, verifier and installer types to `PhotoIdentity.Recognition.Onnx`.
- Added the `PhotoIdentity.Models` command-line tool and PowerShell entry point.
- Added pinned YuNet and SFace manifests with separate code, weights and training-data licence records.
- Added focused tests using deterministic in-memory HTTP responses; CI does not download the model binaries.
