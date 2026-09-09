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


## Handoff from WI-0099 runtime-composition audit

The 2026-09-03 WI-0099 audit confirmed that archive/background-owned PostgreSQL state is implemented, but normal API/worker runtime still contains direct SQLite composition and query dependencies. This is intentionally handed here rather than solved through a partial archive-only provider switch.

In addition to the existing scope above, the WI-0101 inventory must include:
- archive/API/worker constructors that still take or instantiate `SqliteCatalogueDatabase` / SQLite repositories after their state boundary has a PostgreSQL equivalent;
- source scanning/current-revision/general catalogue lookup composition needed by archive analysis and verification;
- archive status/item-filter query repositories where the data belongs to remaining catalogue/library query migration;
- the authoritative automatic Places write path used by GeoNames enrichment;
- normal runtime DI so one provider can be selected coherently before WI-0102 performs the actual migration/cutover.

Do not create dual writes as a bridge. SQLite remains the sole authoritative runtime until the later controlled cutover.

## Current implementation progress

Started 2026-09-09 on the M24 PostgreSQL catalogue branch.

- Added PostgreSQL schema migrations through version 19 for manual photo tags, manual photo people, extended photo metadata/inspection state, first-class Places action/conflict state and saved Smart Collection definitions.
- Added PostgreSQL repositories for manual photo tags, manual photo people, capture metadata, extended metadata and metadata inspection.
- Added a PostgreSQL Places repository for manual place state/actions and automatic place-write precedence/idempotency behind the Core-owned Places contracts.
- Fixed the PostgreSQL schema marker so a clean database initializes idempotently through schema version 17.
- Converted the photo-tag API endpoint and metadata-inspection service constructor to Core-owned persistence contracts instead of concrete SQLite repositories.
- Converted metadata backfill candidate selection to a Core-owned persistence contract and added a PostgreSQL implementation preserving missing/stale/force refresh selection semantics.
- Converted manual photo-people mutations on the photo-details API to the Core-owned `IPhotoPersonRepository` contract instead of endpoint-local SQLite repository construction.
- Converted photo-details reads to a Core-owned persistence contract and added a PostgreSQL implementation preserving confirmed-face/manual-person evidence and metadata join semantics.
- Converted collection photo/manifest queries to a Core-owned persistence contract and added a PostgreSQL implementation preserving confirmed-assignment and top-ranked suggestion semantics.
- Converted saved Smart Collection definition CRUD/listing endpoints to a Core-owned persistence contract and added a PostgreSQL implementation preserving normalized names, filter schema version 2 JSON compatibility and duplicate-name conflict behavior; query/snapshot evaluation remains on SQLite pending the larger follow-up seam.
- Converted source-verification/original-access runtime services from concrete SQLite observation and availability repositories to Core-owned archive source/availability contracts.
- Converted bounded archive analysis coverage reads and availability writes to Core-owned archive coverage/availability contracts while leaving still-unmigrated analysis/status processing collaborators unchanged.
- Converted archive API coverage read/update/start/pause/sync entry points to the Core-owned archive coverage contract, retaining explicit conversion only at still-SQLite status/sync collaborator boundaries.
- Converted the archive advancement worker and face-review derivative backfill coverage flow to the Core-owned archive coverage state, retaining explicit conversion only at the still-SQLite sync coordinator boundary.
- Converted the archive item-filter endpoint coverage read to the Core-owned archive coverage contract while leaving the still-SQLite item-filter query repository unchanged.
- Converted the person-audit API endpoint to the Core-owned `IPersonAuditRepository` contract via the existing SQLite compatibility adapter.
- Converted identity-match regeneration API/worker run and policy state to Core-owned regeneration/policy contracts via existing SQLite adapters, retaining explicit conversion only at still-SQLite evidence and automatic-assignment collaborator boundaries.
- Converted review suggestion list/accept/reject endpoints to the Core-owned `IReviewSuggestionRepository` contract while leaving the still-SQLite face lookup repository unchanged.
- Converted main review create-person/assign/unknown/reject/undo action paths to the Core-owned `IReviewActionRepository` contract while leaving still-SQLite face/filter/image query paths unchanged.
- Converted suggestion-gallery detail action history to the Core-owned `IReviewActionRepository` contract while leaving still-SQLite face/navigation query paths unchanged.
- Converted review face target fallback resolution to the Core-owned `IArchiveReviewProxyRepository` contract while leaving still-SQLite face query inputs unchanged.
- Converted collection review-proxy file resolution to the Core-owned `IArchiveReviewProxyRepository` contract while leaving the face-review derivative resolver on its still-SQLite implementation.
- Added Core-owned Places contracts for manual place state/actions and automatic place writes, then converted the Places API endpoints and reverse-geocode enrichment service to those contracts through the existing SQLite adapters.
- Kept normal runtime binding on SQLite for these newly neutralized surfaces until WI-0102 performs controlled migration/cutover; no dual writes are introduced.
