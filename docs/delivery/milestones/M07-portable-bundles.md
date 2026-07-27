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

## Completion

Pull request [#29](https://github.com/erikwasa/Photo-Identity-Indexer/pull/29) established the model-independent transport and guarded import boundary. Pull request [#30](https://github.com/erikwasa/Photo-Identity-Indexer/pull/30) added production OpenCV, YuNet and SFace processing plus export, process and import commands.

Automated round-trip tests cover every profile, corruption, stale and mismatched results, replay, and preservation of an existing human assignment. The maintainer then exercised the production commands with ignored private media, confirmed that reimport was harmless and the human assignment remained canonical, retained only a privacy-safe aggregate summary, and removed the isolated private verification workspace plus temporary transfer artefacts.

No private paths, photo content, crops, embeddings, hashes, bundle identifiers or revision identifiers were retained in the repository. WI-0018 and M07 completed on 2026-07-27.
