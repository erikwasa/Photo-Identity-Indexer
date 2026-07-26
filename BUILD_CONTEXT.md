# Build context

## Current milestone

**M07 — Portable job bundles**

## Current work item

**WI-0018 — Add portable bundles**

Status: `in_progress`

## Parallel acceptance item

**WI-0015 / M04** remains `in_progress` until the maintainer reports successful Windows and Pixel interaction verification. Pull requests #27 and #28 are merged and the automated verification harness is green, but merge and CI evidence do not prove device comfort or LAN reachability.

## Branch and pull request

- Branch: `agent/WI-0018-portable-bundles`
- Draft pull request: [#29 — Add verified portable job and result bundles](https://github.com/erikwasa/Photo-Identity-Indexer/pull/29)

## Objective

Create self-contained, verifiable work packages that can be processed without the canonical SQLite catalogue and whose returned face results can be imported idempotently without changing human labels.

## Current slice

Establish the model-independent transport boundary. Job and result archives contain a versioned manifest plus checksum-declared payloads. A database-free worker calls an injected processor. The SQLite importer requires the exact original job and result archives, validates the immutable revision, then persists only face occurrences, observations, crops and embeddings.

## Relevant files

- `src/PhotoIdentity.Transfer.Bundles/BundleContracts.cs`
- `src/PhotoIdentity.Transfer.Bundles/PortableBundleArchive.cs`
- `src/PhotoIdentity.Transfer.Bundles/PortableBundleWorker.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteBundleResultImporter.cs`
- `tests/PhotoIdentity.Bundle.Tests/PortableBundleTests.cs`
- `docs/delivery/work-items/WI-0018-portable-bundles.md`
- `docs/delivery/milestones/M07-portable-bundles.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Bundle.Tests/PhotoIdentity.Bundle.Tests.csproj

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

No operator-facing bundle CLI exists in this slice; do not invent an export or import command until the production processor adapter and command boundary are implemented.

## Acceptance test for this slice

- Full-image, reduced-image and face-crop profiles can be verified and processed without SQLite access.
- Unsafe, non-canonical, duplicate, undeclared, missing or checksum-mismatched archive entries are rejected.
- A result is tied to the exact original job-manifest digest and immutable revision metadata.
- Import rejects a mismatched job/result pair and a stale canonical revision.
- Reimporting the same result does not duplicate natural-key face rows.
- Imported model results do not overwrite people, person labels or review actions.
- An existing human assignment remains the current review state after import.
- Atomic archive and crop writes work on Windows.

## Verification

Pull request #28 merged at `2dbb4de34df81ebfe2b326f0bc4fb48369d46b81` with no review findings. GitHub Actions run `30191749014` passed the published review application smoke path, privacy/cache checks and the existing repository workflow. WI-0015 remains open only for explicit target-device acceptance.

The implementation head for draft pull request #29 passed GitHub Actions run `30201002371`, including dependency audit, Release build, all automated tests, living-document validation, generated-document checks, review application smoke and Windows mixed-media verification.

## Known issues

- The production OpenCV/YuNet/SFace inspection pipeline does not yet implement `IPortableBundleProcessor`.
- No CLI commands yet export a canonical revision, process a portable job or import a returned result.
- Imported crop bytes are written before the SQLite transaction. A database failure can leave a verified orphan file, although replay is safe and the deterministic path prevents ambiguity.
- ZIP archives are integrity-checked but not encrypted; transport and retention remain operator responsibilities.
- A real-image round trip must use private ignored fixtures and retain only privacy-safe aggregate evidence.

## Next action

Resolve final CI or review findings on pull request #29. After merge, continue WI-0018 with the production OpenCV/ONNX processor adapter and local export, process and import commands. Keep WI-0015/M04 open until explicit Windows and Pixel verification is reported.
