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
