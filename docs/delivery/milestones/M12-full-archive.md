---
id: M12
title: Full archive processing
status_source: ../status/milestones.yaml
depends_on: [M02, M03, M08, M16]
---

# M12: Full archive processing

## Outcome

A permanent local catalogue is built directly from the real archive under one stable source identity. Coverage can expand incrementally by folder, previously included folders are resynchronised for new or changed photos, unchanged completed analysis is reused, and the eventual full archive can be completed with resumable progress and explicit completeness reporting without requiring the complete OneDrive source archive to remain hydrated on local storage.

The steady-state archive design keeps authoritative originals in OneDrive, compact review proxies and catalogue/model-derived data locally, and only a bounded working set of full-resolution originals hydrated for analysis or explicit viewing.

M12 is broader than the version-1 gate. Version 1 is reached once the permanent-ingestion, bounded-storage and required archive-format capabilities are accepted and the permanent catalogue can safely start. M12 continues until full intended archive coverage is processed or explicitly classified.

## Work items

- [WI-0041](../work-items/WI-0041-incremental-archive-ingestion.md) — stable archive identity and incremental no-repeat ingestion
- [WI-0042](../work-items/WI-0042-bounded-archive-storage.md) — bounded hydration, source verification and durable review proxies
- [WI-0053](../work-items/WI-0053-heic-raw-support.md) — HEIC/HEIF and real-archive RAW support before format-complete permanent ingestion
- [WI-0023](../work-items/WI-0023-full-archive.md) — complete all intended archive coverage

## Version-1 start gate

Before the permanent catalogue is declared ready to begin full-archive creation:

- WI-0042 bounded-storage/OneDrive acceptance is complete;
- WI-0041 permanent incremental-ingestion behavior is complete;
- WI-0053 supports HEIC/HEIF and the RAW variants required by the real archive, with explicit state for any deliberate exception; and
- the product version-1 success criteria are satisfied on the real Windows/OneDrive environment.

## Exit criteria

The permanent ingestion and bounded-storage workflows are proven against the real archive, every intended archive area is explicitly covered or excluded, and every eligible asset has a completed, pending, unavailable, unsupported, permanently failed, deleted or explicitly excluded state. Required HEIC/HEIF and archive RAW variants are represented in that completeness accounting rather than being silently omitted.

Normal review remains possible from permanent local proxies when originals are online-only, full-resolution originals can be hydrated explicitly when needed, local storage stays inside configured safety limits, and no unexplained omissions remain.