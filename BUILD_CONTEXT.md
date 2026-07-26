# Build context

## Current milestone

**M07 — Portable job bundles**

## Current work item

**WI-0018 — Add portable bundles**

Status: `in_progress`

## Completed parallel acceptance item

**WI-0015 / M04** completed on 2026-07-27 after the maintainer reported successful Windows and Pixel trusted-network interaction verification. Pull requests #27 and #28 established the review host and verification harness. Pull request #31 corrected production batch-relative crop resolution found during real-catalogue verification.

## Branch and pull request

- Implementation branch: `agent/WI-0018-production-portable-cli`
- Merged pull requests: [#29 — Add verified portable job and result bundles](https://github.com/erikwasa/Photo-Identity-Indexer/pull/29), [#30 — Add production portable processing commands](https://github.com/erikwasa/Photo-Identity-Indexer/pull/30)
- Related merged review fix: [#31 — Resolve batch-relative review crop paths](https://github.com/erikwasa/Photo-Identity-Indexer/pull/31)

## Objective

Create self-contained, verifiable work packages that can be processed without the canonical SQLite catalogue and whose returned face results can be imported idempotently without changing human labels.

## Current slice

Retain privacy-safe aggregate evidence from the maintainer's private real-image export, process, import and reimport verification, then remove portable archives and disposable working data according to an explicit local retention decision. Do not commit private images, crops, embeddings, hashes, revision identifiers or local paths.

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
- The maintainer has exercised the production commands with private media; only privacy-safe aggregate evidence and cleanup remain before completion.

## Verification

Pull request #29 merged at `8df838dd2764480baf8de87777c019dfdb23ed0e` and established the verified archive format, database-free worker contract and guarded SQLite importer.

Pull request #30 merged at `16c5d8e9bca30ae4ee0e905e1b7d937f2d9ba6d7`. GitHub Actions run `30210084910` passed on the final documented head.

Pull request #31 merged at `7b8e151d74dc1129470e0012ddef20bf609595f7`. GitHub Actions run `30221154431` passed the physical crop-path fix and production-shaped integration coverage.

The maintainer has confirmed export, database-free processing, import, replay safety and preservation of the human assignment using ignored private media.

## Known issues

- The final WI-0018 acceptance record must contain only aggregate facts and no biometric content, hashes, revision identifiers or private paths.
- Model files must be installed separately on the worker; they are not duplicated inside every job archive.
- Face-crop export accepts only already-aligned 112x112 crops and requires explicit canonical face numbers.
- Imported crop bytes are written before the SQLite transaction. A database failure can leave a verified orphan file, although replay is safe and deterministic paths prevent ambiguity.
- ZIP archives are integrity-checked but not encrypted; transport, access control and retention remain operator responsibilities.

## Next action

Record a privacy-safe summary of the real-image round trip, delete the portable job/result archives and disposable processing directories that are no longer needed, and decide whether verified imported crops should be retained with the catalogue. Then mark WI-0018/M07 complete.
