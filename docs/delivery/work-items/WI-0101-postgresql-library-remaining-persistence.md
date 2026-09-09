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


## Remaining work checklist

Current as of 2026-09-09, after local commit `7bcde03`. This is the current implementation checklist; the slice notes below retain historical evidence. Update these checkboxes as each task is implemented and verified, with supporting evidence in the work-item registry. Formal lifecycle status remains in the registry.

Many PostgreSQL repositories already exist. An unchecked integration task does not necessarily require a new repository: reuse the existing Core contract and PostgreSQL implementation where available. A completed repository slice does not establish that normal runtime is free of SQLite dependencies.

### 1. Detector rollout and evaluation

- [x] Move detector review/application records and contracts into Core.
- [x] Add PostgreSQL detector schema, durable inspection payloads and human-resolution history.
- [x] Add PostgreSQL pipeline registration and immutable reconciliation-plan persistence, with live concurrency/replay tests.
- [ ] Implement PostgreSQL unambiguous candidate application, atomically persisting face occurrence, observation, crop, embedding and applied-state evidence without changing person identity assignments.
- [ ] Implement PostgreSQL application of human-reviewed candidates, preserving explicit existing/new/deferred decisions and replay safety.
- [ ] Implement remaining `IDetectorRolloutApplicationRepository` queries: existing/occurrence anchors, pipeline lookup, rollout counts and pending-review reads.
- [ ] Remove concrete SQLite dependencies from `DetectorRolloutProcessing.cs`, rollout CLI composition, pending-review face lookups and crop/file resolution.
- [ ] Finish detector-evaluation session/review catalogue dependencies, including remaining concrete `SqliteDetectorEvaluationRepository` parameters. Audit session, comparison and ground-truth stores to distinguish authoritative state from portable evaluation artifacts and migrate authoritative persistence where required.

### 2. Archive, processing and file access

- [ ] Migrate source scanning/sync and current-revision/general catalogue lookup composition, including `LocalArchiveSyncCoordinator.cs`.
- [ ] Remove remaining concrete SQLite analysis/run/job collaborators from `ArchiveAnalysisProcessing.cs`, `ArchiveBoundedAnalysisService.cs`, `ArchiveEndpoints.cs` and archive advancement flow.
- [ ] Migrate review-proxy and face-review-derivative writers, backfill and file resolvers to provider-neutral persistence. Complete any missing PostgreSQL derivative persistence.
- [ ] Audit `PortableBundleExportCoordinator.cs` and other production catalogue lookup paths; migrate runtime dependencies while explicitly identifying legitimate import/export compatibility boundaries.
- [ ] Verify archive coverage, status/filter paging, availability, hydration, verification, storage accounting and post-analysis operate together through PostgreSQL-backed contracts. Existing individual repositories are not sufficient evidence for this end-to-end composition.

### 3. Review, gallery and identity runtime

- [ ] Replace remaining concrete SQLite face/filter/navigation queries in review, suggestion and gallery endpoints, including `SqliteReviewRepository` and `SqliteReviewFilterRepository` dependencies.
- [ ] Complete provider-neutral crop, face-preview, target and revision resolver dependencies used by review and collections.
- [ ] Connect identity-match regeneration runtime to provider-neutral scoring, evidence-version and automatic-assignment implementations; remove remaining SQLite model/policy conversion and composition dependencies.

### 4. Remaining library and feature-state coverage

- [ ] Verify metadata inspection/backfill, tags, manual people, presentation preferences, Places and reverse-geocode cache/enrichment are fully composed through PostgreSQL, including their workers and lookup collaborators.
- [ ] Audit slideshow snapshots, preparation sessions and leases. Document which state is intentionally transient and which is authoritative; complete required PostgreSQL persistence without silently changing preparation/revalidation semantics.
- [ ] Verify Smart Collection and slideshow snapshot queries expose the required PostgreSQL indexes/query boundary for WI-0108. Slideshow latency acceptance itself remains outside WI-0101.

### 5. Provider selection and startup

- [ ] Complete coherent provider selection in API/worker composition so PostgreSQL mode resolves every authoritative dependency to PostgreSQL implementations, with no dual writes.
- [ ] Remove SQLite schema ensure/migration calls from PostgreSQL startup, including the remaining calls in `Program.cs`; ensure PostgreSQL migrations and readiness checks cover the complete runtime schema.
- [ ] Finish the inventory of direct SQLite types, connections and SQL in normal API/worker paths. Record each remaining occurrence as removed or an explicit migration/import/compatibility/test exception; a namespace import alone is not proof of a runtime dependency.
- [ ] Prove PostgreSQL startup and normal feature/worker operations do not require an authoritative SQLite catalogue. Keep the current production SQLite binding until WI-0102 performs controlled cutover.

### 6. Verification and closure

- [ ] Add/run PostgreSQL behavior-equivalence tests for the remaining domains, including mutations, cancellation, failure handling, transaction rollback and idempotent retries. Prefer repository tests; use HTTP-host tests only for composition/contracts requiring that layer.
- [ ] Verify migrations on clean and existing supported PostgreSQL schemas and run live tests with PostgreSQL explicitly configured; opt-in tests returning early do not prove PostgreSQL acceptance.
- [ ] Run the relevant solution build/tests and published-runtime checks for the completed composition; record test scope, outcomes and material timing in the registry.
- [ ] Review logging/privacy, migration coverage and allowed SQLite exceptions, then verify every acceptance criterion above against the final runtime.
- [ ] Update the handoff and affected documentation, pass `PhotoIdentity.Docs validate` and `generate --check`, and transition WI-0101 through review to completion only when the full item is verified.

### Work owned by later items

Existing-catalogue import, production cutover and rollback acceptance belong to WI-0102. Match-regeneration scaling belongs to WI-0103; operator UI/query performance to WI-0104; slideshow latency fixes to WI-0108; operational backup/recovery and real-archive catch-up acceptance to WI-0106. WI-0101 must provide the complete persistence/runtime boundary those items depend on.

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

- Added PostgreSQL schema migrations through version 20 for manual photo tags, manual photo people, extended photo metadata/inspection state, first-class Places action/conflict state, saved Smart Collection definitions and person presentation preferences.
- Added PostgreSQL repositories for manual photo tags, manual photo people, capture metadata, extended metadata and metadata inspection.
- Added a PostgreSQL Places repository for manual place state/actions and automatic place-write precedence/idempotency behind the Core-owned Places contracts.
- Fixed the PostgreSQL schema marker so a clean database initializes idempotently through schema version 17.
- Converted the photo-tag API endpoint and metadata-inspection service constructor to Core-owned persistence contracts instead of concrete SQLite repositories.
- Converted metadata backfill candidate selection to a Core-owned persistence contract and added a PostgreSQL implementation preserving missing/stale/force refresh selection semantics.
- Converted manual photo-people mutations on the photo-details API to the Core-owned `IPhotoPersonRepository` contract instead of endpoint-local SQLite repository construction.
- Converted photo-details reads to a Core-owned persistence contract and added a PostgreSQL implementation preserving confirmed-face/manual-person evidence and metadata join semantics.
- Converted collection photo/manifest queries to a Core-owned persistence contract and added a PostgreSQL implementation preserving confirmed-assignment and top-ranked suggestion semantics.
- Converted saved Smart Collection definition CRUD/listing endpoints to a Core-owned persistence contract and added a PostgreSQL implementation preserving normalized names, filter schema version 2 JSON compatibility and duplicate-name conflict behavior.
- Converted Smart Collection ad-hoc/saved query and slideshow snapshot creation to a Core-owned persistence contract and added a PostgreSQL implementation preserving people/tag match modes, manual photo-person evidence, named place ancestry, GPS/taken filters and slideshow chronology semantics.
- Converted detector-evaluation run/photo/detection catalogue reads to a Core-owned persistence contract and added a PostgreSQL implementation preserving run summaries, staged photo ordering and latest-observation bounding box compatibility.
- Converted person favorites, smart-collection visibility, active photo counts and representative/featured-face reads and writes to Core-owned persistence contracts and added a PostgreSQL implementation preserving favorite sorting, hidden-person filtering, manual/confirmed photo count evidence and featured-face fallback/explicit selection semantics.
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
- Added a Core-owned archive status/query contract covering folder rollups, state-filtered item paging, orthogonal availability/verification/analysis item filtering and latest archive-analysis run status.
- Converted archive status and archive item-filter API reads to the Core-owned archive status/query contract; default DI still resolves it to SQLite until controlled provider cutover.
- Added `PostgresArchiveStatusRepository` preserving current archive folder counts, unavailable-vs-pending classification differences between the status and orthogonal filter endpoints, failed-job error surfacing, pagination totals and latest-run job counts.
- Kept normal runtime binding on SQLite for these newly neutralized surfaces until WI-0102 performs controlled migration/cutover; no dual writes are introduced.

## Detector rollout contract slice (2026-09-09)

- Moved detector pipeline, candidate inspection, reconciliation review/resolution and rollout orchestration records into `PhotoIdentity.Core.Recognition`. This explicitly changes their CLR namespace from the SQLite assembly; HTTP payloads and stored representations are unchanged.
- Added `IDetectorRolloutReviewRepository` and `IDetectorRolloutApplicationRepository`, implemented by the existing SQLite repositories. Detector rollout endpoints now consume these contracts, and run-summary reads consume `IProcessingExecutionRepository`.
- Existing repository tests exercise the new interfaces; the existing HTTP tests verify runtime DI. No additional host-heavy tests or required CI gates were introduced.
- PostgreSQL detector schema/repositories, rollout worker/CLI composition and the pending-review face lookup remain unfinished. This slice is not PostgreSQL detector acceptance or provider cutover.

## PostgreSQL detector review slice (2026-09-09)

- Added schema version 21 for detector pipelines, run provenance, reconciliation plans/candidates/options/unmatched faces, durable candidate inspections and append-only resolution actions. UUID keys, JSONB geometry, binary embedding payloads, timestamp precision, foreign keys and pending/history indexes preserve the existing storage semantics on PostgreSQL.
- Added `PostgresDetectorRolloutReviewRepository` implementing the Core review contract. Candidate-row locks serialize inspection writes and resolution retries; applied candidates reject further mutation. Timestamp comparison accounts for PostgreSQL microsecond precision so valid .NET timestamps remain replayable.
- Added a repository-level live PostgreSQL test covering immutable payload round-trips, concurrent identical writes, resolution history, invalid options, pending reviews, cancellation and applied-state rejection. It uses an isolated disposable database and does not start the HTTP host. The existing schema migration test and new review test passed together against live PostgreSQL (2 tests, 3 seconds).
- Required CI gates are unchanged. The live test follows the existing `PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` opt-in convention; an ordinary test run without that setting does not establish live PostgreSQL acceptance.
- Remaining detector work: pipeline registration/plan persistence, unambiguous and reviewed candidate application, application queries, and worker/CLI composition. Normal runtime still binds SQLite until controlled cutover.
- Local verification: `./build.ps1` passed with zero warnings/errors; `./test.ps1` passed 533 tests, including 402 integration tests in 1 minute 39 seconds; `./verify-review.ps1 -Mode Smoke -Configuration Release -SkipBuild` passed the comprehensive published-app smoke checks. Documentation validation and generated-output checks also passed.

## PostgreSQL detector plan slice (2026-09-09)

- Added the Core-owned `IDetectorReconciliationPlanRepository` contract and PostgreSQL implementation for exact pipeline registration, immutable plan persistence and plan reads. The existing SQLite repository also implements this contract. Schema version 21 already contains the required tables; no new migration is needed.
- Pipeline registration preserves the first canonical definition and prevents rebinding a processing run. Concurrent identical plan writes converge on the committed plan. PostgreSQL reads collect candidate options in the candidate query and use a repeatable-read transaction for a coherent plan snapshot.
- Explicit contract correction: replay cannot append candidates/options/unmatched evidence to an existing plan. Both providers now compare the existing plan before writing child rows. Applied plans continue to reject re-saving; their application evidence remains readable. PostgreSQL comparisons account for microsecond timestamp precision.
- Added repository-layer tests for changed-plan rollback, exact provenance, concurrent replay, all three candidate dispositions, geometry/options/unmatched evidence, cancellation and applied-state rejection. The two detector PostgreSQL tests passed against isolated live databases (859 ms), and all 19 detector rollout integration/repository tests passed (4 seconds). No new HTTP-host tests or required CI gates were added.
- Remaining detector work is unambiguous/reviewed candidate application, application queries and worker/CLI composition. SQLite remains the authoritative runtime pending WI-0102.
- Final local verification: `./test.ps1` rebuilt the Release solution and passed 535 tests, including 403 integration tests in 1 minute 40 seconds. `PhotoIdentity.Docs validate`, `generate --check` and `git diff --check` passed.
