---
id: M12
title: Full archive processing
status_source: ../status/milestones.yaml
depends_on: [M02, M03, M08, M16]
---

# M12: Full archive processing

## Outcome

A permanent local catalogue is built directly from the real archive under one stable source identity. Coverage can expand incrementally by folder, previously included folders are resynchronised for new or changed photos, unchanged completed analysis is reused, and the eventual full archive can be completed with resumable progress and explicit completeness reporting.

## Work items

- [WI-0041](../work-items/WI-0041-incremental-archive-ingestion.md)
- [WI-0023](../work-items/WI-0023-full-archive.md)

## Exit criteria

The permanent ingestion workflow is proven against the real archive, every intended archive area is explicitly covered or excluded, and every eligible asset has a completed, pending, unavailable, unsupported, permanently failed, deleted or explicitly excluded state. No unexplained omissions remain.
