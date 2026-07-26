# Build context

## Current milestone

**M07 — Portable job bundles**

## Current work item

**WI-0018 — Add portable bundles**

Status: `in_progress`

## Parallel acceptance item

**WI-0015 / M04** remains `in_progress` until the maintainer reports successful Windows and Pixel interaction verification. Pull requests #27 and #28 are merged and the automated verification harness is green, but merge and CI evidence do not prove device comfort or LAN reachability.

## Branch and pull request

- Branch: `agent/WI-0018-production-portable-cli`
- Draft pull request: [#30 — Add production portable processing commands](https://github.com/erikwasa/Photo-Identity-Indexer/pull/30)

## Objective

Create self-contained, verifiable work packages that can be processed without the canonical SQLite catalogue and whose returned face results can be imported idempotently without changing human labels.

## Current slice

Connect the verified bundle format to production OpenCV, YuNet and SFace processing. A database-backed exporter verifies the immutable revision and writes full-image, reduced-image or explicitly numbered aligned-crop jobs. A database-free processor reads signed inference configuration, produces result crops and embeddings, and a CLI exposes export, process and import operations.

## Relevant files

- `src/PhotoIdentity.Transfer.Bundles/BundleContracts.cs`
- `src/PhotoIdentity.Transfer.Bundles/PortableBundleArchive.cs`
- `src/PhotoIdentity.Transfer.Bundles/PortableBundleWorker.cs`
- `src/PhotoIdentity.Worker/PortableRecognitionProcessor.cs`
- `src/PhotoIdentity.Worker/PortableBundleExportCoordinator.cs`
- `src/PhotoIdentity.Cli/BundleCommand.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteBundleResultImporter.cs`
- `tests/PhotoIdentity.Bundle.Tests/PortableBundleTests.cs`
- `tests/PhotoIdentity.Integration.Tests/PortableBundleCommandTests.cs`
- `tests/PhotoIdentity.Integration.Tests/PortableFaceCropOrdinalTests.cs`
- `docs/delivery/work-items/WI-0018-portable-bundles.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  bundle export `
  --database "C:\PhotoIdentity\catalogue.db" `
  --revision REVISION_ID `
  --job "C:\PhotoIdentity\transfer\job.photoid-job"

dotnet run --project src/PhotoIdentity.Cli -- `
  bundle process `
  --job "C:\PhotoIdentity\transfer\job.photoid-job" `
  --result "C:\PhotoIdentity\transfer\result.photoid-result"

dotnet run --project src/PhotoIdentity.Cli -- `
  bundle import `
  --database "C:\PhotoIdentity\catalogue.db" `
  --job "C:\PhotoIdentity\transfer\job.photoid-job" `
  --result "C:\PhotoIdentity\transfer\result.photoid-result" `
  --output "C:\PhotoIdentity\bundle-imports"

dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

For crop-only work, every input must include the canonical one-based face number, for example `--crop "3=C:\PhotoIdentity\crops\face-003.png"`. The signed archive path preserves occurrence ordinal `2`; list order is never used as identity.

## Acceptance test for this slice

- Full-image and reduced-image jobs run the production decode, YuNet, alignment and SFace boundaries without SQLite access.
- Face-crop jobs embed already-aligned 112x112 inputs without claiming a YuNet observation.
- The confidence threshold is read from the verified job configuration; process-time overrides are rejected.
- Full and reduced exports verify that the local source still matches the immutable revision hash.
- Explicit crop face numbers survive transport, including non-first ordinals, and duplicates are rejected.
- Import requires the exact job/result pair and current immutable revision hash.
- Reimport remains harmless and existing human review state remains canonical.
- All existing review-host and Windows mixed-media verification gates remain green.

## Verification

Pull request #29 merged at `8df838dd2764480baf8de87777c019dfdb23ed0e` with no comments, reviews or unresolved threads. It established the verified archive format, database-free worker contract and guarded SQLite importer.

Draft pull request #30 implementation head passed GitHub Actions run `30209633129`, including dependency audit, Release build, all automated tests, living-document validation, generated-document checks, review application smoke and Windows mixed-media verification.

## Known issues

- The final WI-0018 acceptance step requires a real private-image export, process and import run. Only a privacy-safe aggregate summary should be retained.
- Model files must be installed separately on the worker; they are not duplicated inside every job archive.
- Face-crop export accepts only already-aligned 112x112 crops and requires explicit canonical face numbers.
- Imported crop bytes are written before the SQLite transaction. A database failure can leave a verified orphan file, although replay is safe and deterministic paths prevent ambiguity.
- ZIP archives are integrity-checked but not encrypted; transport, access control and retention remain operator responsibilities.

## Next action

Resolve final CI or review findings on pull request #30. After merge, add a privacy-safe local verification command or script for a real-image full/reduced round trip. Keep WI-0018/M07 open until that evidence is reported, and keep WI-0015/M04 open until explicit Windows and Pixel verification is reported.
