---
id: WI-0101
title: Migrate library and remaining authoritative persistence to PostgreSQL
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0098]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Sqlite]
---

# WI-0101: Migrate library and remaining authoritative persistence to PostgreSQL

## Objective
Complete PostgreSQL coverage for the remaining authoritative application domains so normal runtime no longer depends on SQLite catalogue writes.

## In scope
- Photo metadata/inspection/backfill state, tags, Places and reverse-geocode cache/enrichment records.
- Smart collections, manual photo people and visibility preferences.
- Slideshow snapshots/preparation state.
- Detector evaluation/rollout/reconciliation persistence.
- Collection/photo detail queries and remaining catalogue repositories.
- Replace SQLite-specific schema ensure calls in normal runtime with PostgreSQL migrations/contracts.
- Inventory and eliminate direct `Microsoft.Data.Sqlite` use from normal API/worker runtime, excluding migration/import compatibility code.
- Preserve current Smart Collection/slideshow semantics while exposing PostgreSQL query/index opportunities needed by WI-0108.

## Performance boundary

WI-0101 is a persistence migration item, not acceptance of slideshow responsiveness. PostgreSQL may reduce snapshot/query contention or scan cost, but the 2026-09-02 real-phone findings for slow `/slideshows` loading, slow first-image startup and slow image transitions remain owned by WI-0108. Database-independent repeated original hashing/file-serving work is explicitly outside the claim that this migration alone fixes performance.

## Acceptance criteria
- [ ] All authoritative runtime domains have PostgreSQL implementations.
- [ ] Normal PostgreSQL mode performs no SQLite authoritative read/write dependency.
- [ ] Remaining SQLite code is explicitly limited to migration/import/compatibility/test scenarios.
- [ ] Existing feature integration tests remain behaviorally equivalent on PostgreSQL.
- [ ] Smart Collection/slideshow persistence exposes the indexes/query boundary needed for WI-0108 without claiming the WI-0108 latency acceptance itself.
