---
id: M12
title: Full archive processing
status_source: ../status/milestones.yaml
depends_on: [M02, M03, M08, M16]
---

# M12: Full archive processing

## Outcome

A permanent local catalogue is built directly from the real archive under one stable source identity. Coverage can expand incrementally by folder, previously included folders are resynchronised for new or changed photos, unchanged completed analysis is reused, and the archive can be processed with resumable progress and explicit completeness reporting without requiring the complete OneDrive source archive to remain hydrated on local storage.

The steady-state archive design keeps authoritative originals in OneDrive, compact review proxies and catalogue/model-derived data locally, and only a bounded working set of full-resolution originals hydrated for analysis or explicit viewing.

## Work items

- [WI-0041](../work-items/WI-0041-incremental-archive-ingestion.md) — stable archive identity and incremental no-repeat ingestion
- [WI-0042](../work-items/WI-0042-bounded-archive-storage.md) — bounded hydration, source verification and durable review proxies
- [WI-0053](../work-items/WI-0053-heic-raw-support.md) — HEIC/HEIF and real-archive RAW support before format-complete permanent ingestion
- [WI-0054](../work-items/WI-0054-archive-ui-polish.md) — accepted viewer, progress and availability polish discovered during real-archive verification
- [WI-0023](../work-items/WI-0023-full-archive.md) — historical full-coverage item, superseded by M24/WI-0106

## Closeout — 2026-09-12

The archive-readiness work in WI-0041, WI-0042, WI-0053 and WI-0054 was already completed and human-verified. The later full-coverage objective was then achieved operationally during M24 rather than through the original WI-0023 batch plan.

WI-0106 completed the real PostgreSQL production catch-up with all current archive images analysed and no failed/unverified backlog, then verified a small daily-style increment without reprocessing the already-complete catalogue. WI-0023 is therefore closed as superseded, and M12 can close without rerunning a duplicate full-archive procedure.

This closeout does not change the permanent archive architecture: authoritative originals remain in OneDrive, local materialization remains bounded, and the production catalogue remains the same continuously evolved catalogue.

## Exit criteria

The permanent ingestion and bounded-storage workflows are proven against the real archive, intended archive coverage has reached production steady state, and required HEIC/HEIF/archive-format handling participates in completeness accounting rather than being silently omitted.

Normal review remains possible from permanent local proxies when originals are online-only, full-resolution originals can be hydrated explicitly when needed, local storage stays inside configured safety limits, and the accepted PostgreSQL production catalogue can continue with incremental operation.
