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

Pull request [#29](https://github.com/erikwasa/Photo-Identity-Indexer/pull/29) established the model-independent transport and guarded import boundary. Pull request [#30](https://github.com/erikwasa/Photo-Identity-Indexer/pull/30) added production OpenCV, YuNet and SFace processing plus export, process and import commands.

Automated round-trip tests cover every profile, corruption, stale and mismatched results, replay, and preservation of an existing human assignment. The maintainer has now exercised the production commands with ignored private media and confirmed that reimport is harmless and the human assignment remains canonical.

## Remaining milestone scope

M07 remains in progress until a privacy-safe aggregate summary is retained and temporary job/result archives plus disposable processing directories are cleaned up according to an explicit local retention decision. The evidence must not contain private paths, photo content, crops, embeddings, hashes or revision identifiers.

M04 completed independently on 2026-07-27 after successful Windows and Pixel trusted-network interaction verification for WI-0015.
