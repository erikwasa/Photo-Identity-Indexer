---
id: WI-0018
title: Add portable bundles
milestone: M07
status_source: ../status/work-items.yaml
depends_on: [WI-0013]
affected_modules: [PhotoIdentity.Transfer.Bundles, PhotoIdentity.Worker, PhotoIdentity.Cli, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Bundle.Tests, PhotoIdentity.Integration.Tests]
---

# WI-0018: Add portable bundles

## Objective

Implement job and result bundles with manifests, checksums, full-image, reduced-image and face-crop profiles, production processing commands and idempotent result import.

## Acceptance criteria

- [x] A worker processes a bundle without database access.
- [x] Corrupt or stale results are rejected.
- [x] Reimporting the same bundle is harmless.
- [x] Human labels are unaffected by bundle import.
- [x] Operator commands export, process and import verified bundles.
- [ ] A privacy-safe real-image round trip exercises the production commands.

The model-independent and production command paths are automated. WI-0018 remains in progress until a maintainer runs the commands against ignored private media and retains only non-biometric aggregate evidence.

## Bundle format

Portable jobs and results are versioned ZIP archives with a canonical `manifest.json`. Every payload is declared with:

- a canonical forward-slash archive path;
- a role describing its processing purpose;
- an exact byte count;
- a SHA-256 digest.

Archive extraction rejects traversal paths, non-canonical paths, case-insensitive collisions, duplicate entries, undeclared files, missing payloads and checksum or length mismatches. Job configuration is stored as JSON inside the verified manifest. The production processor reads the YuNet confidence threshold from that signed job configuration; process-time overrides are rejected.

## Job profiles

The format supports three transfer profiles:

- `FullImage` transports exactly one source image whose payload hash must equal the immutable catalogue revision hash;
- `ReducedImage` transports exactly one bounded, normalised PNG while retaining the original revision hash in the manifest;
- `FaceCrops` transports one or more already-aligned 112x112 PNG crops while retaining the original revision hash.

Full-image and reduced-image jobs run OpenCV decoding, YuNet detection, deterministic face ordering, five-point SFace alignment and SFace embedding without accessing SQLite. Face-crop jobs bypass detection and alignment, embed the already-aligned inputs, and use the explicit provenance model identifier `portable-aligned-face-crop-v1` rather than claiming a YuNet observation.

Every face-crop input carries its canonical one-based face number in a signed path such as `inputs/faces/face-003.png`. The worker converts that to occurrence ordinal `2`. Export rejects missing or duplicate face numbers, preventing transport order from attaching results to the wrong human-reviewed face.

`PortableBundleWorker` has no database dependency. It verifies and extracts a job into a disposable directory, calls `PortableRecognitionProcessor`, then writes a result bundle. Job and result paths cannot overlap the disposable working directory or each other.

## Operator commands

Export a full-image job from a canonical revision:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  bundle export `
  --database "C:\PhotoIdentity\catalogue.db" `
  --revision REVISION_ID `
  --job "C:\PhotoIdentity\transfer\job.photoid-job"
```

Use `--profile reduced-image --max-width 1600 --max-height 1600` to transport a bounded PNG. For pre-aligned crops, use explicit canonical face numbers:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  bundle export `
  --database "C:\PhotoIdentity\catalogue.db" `
  --revision REVISION_ID `
  --profile face-crops `
  --crop "1=C:\PhotoIdentity\crops\face-001.png" `
  --crop "3=C:\PhotoIdentity\crops\face-003.png" `
  --job "C:\PhotoIdentity\transfer\crop-job.photoid-job"
```

Process without the canonical database:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  bundle process `
  --job "C:\PhotoIdentity\transfer\job.photoid-job" `
  --result "C:\PhotoIdentity\transfer\result.photoid-result"
```

The worker machine must have the pinned YuNet and SFace model files installed. Model binaries are not copied into every job bundle.

Import the exact verified job/result pair:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  bundle import `
  --database "C:\PhotoIdentity\catalogue.db" `
  --job "C:\PhotoIdentity\transfer\job.photoid-job" `
  --result "C:\PhotoIdentity\transfer\result.photoid-result" `
  --output "C:\PhotoIdentity\bundle-imports"
```

## Result trust boundary

A result manifest records the exact raw job-manifest digest, bundle identifier, asset revision, source content hash and profile. Result payloads are restricted to one declared crop per face result. Model identifiers, model hashes, alignment protocol, geometry, confidence and embeddings are retained for reproducibility.

`SqliteBundleResultImporter` requires both the original job archive and its result archive. Before importing, it verifies:

1. every archive payload and manifest;
2. exact result-to-job manifest linkage;
3. the immutable asset revision identifier and content hash against the canonical catalogue;
4. deterministic crop storage bytes and hashes.

The importer writes only face occurrences, observations, crops and embeddings. It does not write `people`, `person_labels` or `review_actions`. Existing natural keys make replay harmless, and an existing human assignment remains the current review state when a model result for the same face ordinal is imported.

## Validation

Automated coverage includes:

- database-free processing for all three profiles;
- production image detection, alignment and embedding boundaries with deterministic substitutes;
- signed confidence configuration and rejection of process-time overrides;
- reduced-image export and immutable source-hash verification;
- explicit non-first face-crop ordinal preservation and duplicate-number rejection;
- corrupted job and result payload rejection;
- mismatched job/result and stale canonical revision rejection;
- replay-safe result import and preservation of an existing human assignment;
- CLI export and import round trips;
- Windows-compatible atomic archive and crop writes.

Pull request [#29](https://github.com/erikwasa/Photo-Identity-Indexer/pull/29) merged the verified archive, database-free worker contract and guarded importer at merge commit `8df838dd2764480baf8de87777c019dfdb23ed0e`.

Draft pull request [#30](https://github.com/erikwasa/Photo-Identity-Indexer/pull/30) adds the production processor, exporter and operator commands. GitHub Actions run `30209633129` passed dependency restore and audit, Release build, all tests, documentation checks, the published review application smoke test and Windows mixed-media verification on the implementation head.

## Remaining work

- Run a real private-image full or reduced job through export, process and import, then retain only a privacy-safe summary.
- Define cleanup and retention policy for exported jobs, returned results, disposable working directories and verified imported crops.
- Imported crop bytes are written before the SQLite transaction; a database failure can leave a verified orphan file. Deterministic paths and replay-safe natural keys make recovery unambiguous, but automated orphan cleanup is not included in this slice.
- ZIP archives are integrity-checked but not encrypted; transport, access control and retention remain operator responsibilities.
