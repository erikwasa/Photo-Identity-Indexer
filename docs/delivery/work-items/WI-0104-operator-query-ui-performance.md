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
- [ ] Selecting a face checkbox does not trigger server I/O and remains responsive with a large loaded review set.
- [ ] A single face action does not reload all currently loaded cards.
- [ ] Face Gallery page/scroll work is bounded to the requested page plus justified summary work.
- [ ] Gallery images are not OpenCV-resized/re-encoded on every unchanged request.
- [ ] Settings shell/configuration can render without waiting for full archive status aggregation.
