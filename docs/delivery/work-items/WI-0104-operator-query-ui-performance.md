---
id: WI-0104
title: Remove known operator query and UI scaling bottlenecks
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0100, WI-0101]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres]
---

# WI-0104: Remove known operator query and UI scaling bottlenecks

## Objective
Correct the catalogue-size-dependent delays already identified in Face Review, Face Gallery and Settings after PostgreSQL persistence is available.

## In scope
- Single face actions update local loaded state/backfill instead of reloading the entire loaded face set.
- Avoid rendering one full people `<select>` list in every mounted face card; use a lazy/searchable assignment control or equivalent bounded rendering.
- Rework gallery/review queries so page and total calculation do not repeatedly execute expensive whole-catalogue current-state/window scans.
- Use appropriate PostgreSQL indexes/current-state projections/query shapes.
- Eliminate repeated request-time OpenCV downsizing/re-encoding for gallery thumbnails via direct durable serving or a generated-once cache.
- Add cache validators/headers for stable face derivatives where safe.
- Add a cheap archive-configuration Settings endpoint and load independent Settings sections independently rather than blocking the whole page on full archive status/storage/filter work.

## Acceptance criteria
- [x] Selecting a face checkbox does not trigger server I/O and remains responsive with a large loaded review set.
- [x] A single face action does not reload all currently loaded cards.
- [x] Face Gallery page/scroll work is bounded to the requested page plus justified summary work.
- [ ] Gallery images are not OpenCV-resized/re-encoded on every unchanged request.
- [ ] Settings shell/configuration can render without waiting for full archive status aggregation.

## Progress evidence

- PR #284 replaced the per-card full people list with a bounded lazy/searchable assignment picker, kept checkbox/range selection local and replaced whole-loaded-set single-action reloads with one-card reconciliation plus bounded backfill.
- Representative PostgreSQL plan capture at schema 23 measured 18,281 face occurrences, 10,366 rank-one suggestions and 8,702 active review actions. Before the query correction, the exact count paths completed in about 7.5 ms, while the 40-row Needs Review page performed 18,281 latest-action probes plus 9,584 suggestion lookups before the page limit (about 806 ms for Suggested person and 255 ms for Newest first).
- PR #293 moved latest review-action enrichment to the bounded detail phase and changed page/navigation membership to the same set-based review-state predicates used by exact counts. The 2026-09-11 rerun against the same schema-23 catalogue measured 32.397 ms for Suggested person, 19.567 ms for Newest first, 3.973 ms for the all-confidence count and 2.821 ms for the high-confidence count. The page plans use a set-based anti join plus in-memory top-N sorting, incur crop/observation/action lookups only for the final 40 rows, perform no temporary I/O and no longer execute a latest-action probe for every face. No additional PostgreSQL index is justified by this representative evidence.
- The current image slice reuses the existing durable 960px contextual face derivative and adds a generated-once private 360px server-side response cache for gallery/person-card requests. The established `/api/review` `Cache-Control: no-store` boundary is intentionally retained; the optimization avoids repeated OpenCV work without permitting browser/proxy persistence of review imagery.

## Boundary with slideshow performance

WI-0104 remains focused on Face Review, Face Gallery and Settings. The slideshow-library/start/playback latency observed during M22 acceptance is tracked separately by WI-0108 so it receives explicit investigation and acceptance rather than being implied by this operator-UI item.
