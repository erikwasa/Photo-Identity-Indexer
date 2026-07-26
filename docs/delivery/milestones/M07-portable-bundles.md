---
id: M07
title: Portable job bundles
status_source: ../status/milestones.yaml
depends_on: [M02]
---

# M07: Portable job bundles

## Outcome

A worker can process self-contained full-image, reduced-image or crop-only bundles without accessing the canonical database.

## Work items

- [WI-0018](../work-items/WI-0018-portable-bundles.md)

## Exit criteria

- Checksums and manifests are verified.
- Results import idempotently.
- Changed revisions are rejected.
- Canonical labels survive imports and reimports.

## Current work

Draft pull request [#29](https://github.com/erikwasa/Photo-Identity-Indexer/pull/29) establishes the model-independent transport and import boundary:

- versioned, checksum-declared job and result archives;
- full-image, reduced-image and face-crop profiles;
- a database-free worker contract;
- exact result-to-job linkage;
- canonical revision validation before SQLite import;
- replay-safe face persistence without human-label writes.

Automated round-trip tests cover every profile, corruption, stale and mismatched results, replay, and preservation of an existing human assignment. GitHub Actions run `30201002371` passed the full repository workflow on the implementation head.

## Remaining milestone scope

M07 remains in progress. The next slice must connect the production OpenCV/ONNX inspection pipeline to the database-free processor contract and expose local export, process and import commands. Completion also requires a privacy-safe real-image round trip and an explicit bundle-retention policy.

M04 remains independently in progress until the maintainer reports successful Windows and Pixel interaction verification for WI-0015.
