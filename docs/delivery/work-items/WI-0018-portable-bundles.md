---
id: WI-0018
title: Add portable bundles
milestone: M07
status_source: ../status/work-items.yaml
depends_on: [WI-0013]
affected_modules: [PhotoIdentity.Transfer.Bundles, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Bundle.Tests]
---

# WI-0018: Add portable bundles

## Objective

Implement job and result bundles with manifests, checksums, full-image, reduced-image and face-crop profiles, plus idempotent result import.

## Acceptance criteria

- [x] A worker processes a bundle without database access.
- [x] Corrupt or stale results are rejected.
- [x] Reimporting the same bundle is harmless.
- [x] Human labels are unaffected by bundle import.

The model-independent round-trip criteria are automated. WI-0018 remains in progress because the production OpenCV/ONNX processor adapter and operator-facing export, process and import commands are still required before M07 is complete.

## Bundle format

Portable jobs and results are versioned ZIP archives with a canonical `manifest.json`. Every payload is declared with:

- a canonical forward-slash archive path;
- a role describing its processing purpose;
- an exact byte count;
- a SHA-256 digest.

Archive extraction rejects traversal paths, non-canonical paths, case-insensitive collisions, duplicate entries, undeclared files, missing payloads and checksum or length mismatches. Job configuration is stored as opaque JSON but must parse as valid JSON.

## Job profiles

The format supports three transfer profiles:

- `FullImage` transports exactly one source image whose payload hash must equal the immutable catalogue revision hash;
- `ReducedImage` transports exactly one reduced image while retaining the original revision hash in the manifest;
- `FaceCrops` transports one or more pre-extracted face crops while retaining the original revision hash.

`PortableBundleWorker` has no SQLite dependency. It verifies and extracts a job into a disposable directory, calls an injected `IPortableBundleProcessor`, then writes a result bundle. Job and result paths cannot overlap the disposable working directory or each other.

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
- corrupted job and result payload rejection;
- mismatched job/result rejection;
- stale canonical revision rejection;
- replay-safe result import;
- preservation of an existing human assignment;
- Windows-compatible atomic archive and crop writes.

GitHub Actions run `30201002371` passed dependency restore and audit, Release build, all tests, documentation checks, the published review application smoke test and Windows mixed-media verification.

Draft pull request [#29](https://github.com/erikwasa/Photo-Identity-Indexer/pull/29) contains this first M07 vertical slice.

## Remaining work

- Adapt the production OpenCV/YuNet/SFace inspection pipeline to `IPortableBundleProcessor`.
- Add operator-facing CLI commands to export a canonical revision, process a portable job and import a verified result.
- Add a real-image local round trip that does not commit personal media or biometric artefacts.
- Define cleanup and retention policy for exported jobs, returned results and verified imported crops.
