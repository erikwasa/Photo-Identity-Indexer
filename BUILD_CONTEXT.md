# Build context

## Current milestone

**M01 — Single-image inference**

## Current work item

**WI-0010 — Build photoid inspect command**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0010-inspect-command`
- Draft pull request: [#16 — Build photoid inspect command](https://github.com/erikwasa/Photo-Identity-Indexer/pull/16)

## Objective

Compose decoding, YuNet detection, padded review crops, five-point SFace alignment and SFace embeddings for one JPEG or PNG, then write visual and reproducible inspection outputs without modifying the source.

## Relevant files

- `src/PhotoIdentity.Cli/Program.cs`
- `src/PhotoIdentity.Cli/DecodeCommand.cs`
- `src/PhotoIdentity.Cli/InspectCommand.cs`
- `src/PhotoIdentity.Cli/Properties/AssemblyInfo.cs`
- `tests/PhotoIdentity.Integration.Tests/InspectCommandTests.cs`
- `docs/delivery/work-items/WI-0010-inspect-command.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

After merge, run the complete private M01 verification:

```powershell
./models/install-models.ps1 -Id yunet-2023mar-fp32,sface-2021dec-fp32

dotnet run --project src/PhotoIdentity.Cli -- `
  inspect "C:\PrivateVerification\family-photo.jpg" `
  --output ".artifacts\inspect\family-photo" `
  --overwrite `
  --verbose
```

## Acceptance test

- JPEG and PNG inputs are decoded with EXIF orientation handling.
- The pinned YuNet and SFace manifests and installed files are located from the repository or explicit paths.
- Detections are ordered deterministically and annotated with boxes, confidence and five landmark labels.
- Each face writes a bounds-clamped padded crop, a fixed 112×112 aligned crop and a 128-dimensional L2-normalised embedding.
- `manifest.json` records source/model hashes, preprocessing metadata, geometry and deterministic output hashes.
- `timings.json` records decode, detector and per-face stage durations separately from reproducibility data.
- Exit codes distinguish usage, media, model and inference failures.
- The source SHA-256 is unchanged after processing, and unsafe output-directory deletion is rejected.
- Synthetic integration coverage exercises the complete output path without model downloads or private fixtures.

## Verification

WI-0009 is implementation-complete. Pull request #15 merged at `19b36537368304f4b7c11bd330f6e6089338eca6`, and GitHub Actions run `30168578069` passed the final Windows workflow.

The first PR #16 workflow passed the Release build, all automated tests and living-document checks for the command implementation before canonical status updates were applied. Final branch evidence will use the latest workflow head.

## Known issues

- The current execution providers are CPU-only.
- Real model binaries and private photos are deliberately absent from CI.
- `annotated.svg` is used instead of a raster overlay so the normalised source can be embedded without exposing OpenCV types outside the imaging adapter.
- M01 is not complete until the merged command is run locally on representative private JPEG and PNG files.

## Next action

Resolve any final CI findings on pull request #16, merge it, then run the consolidated M01 local verification for visual geometry, same-person/different-person cosine scores, repeated CPU inference and source integrity.