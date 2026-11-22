---
id: WI-0078
title: Reprocess stale photo metadata after extraction-contract changes
milestone: M20
status_source: ../status/work-items.yaml
depends_on: [WI-0072]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Source.Local, documentation]
---

# WI-0078: Reprocess stale photo metadata after extraction-contract changes

## Objective

Ensure catalogue revisions that were inspected by an older metadata reader are automatically eligible for bounded re-inspection when Photo Identity adds or changes supported metadata fields.

Before this item, the backfill selector only chose revisions with no `photo_capture_metadata` row. A revision inspected before WI-0072 therefore looked complete even though newer fields such as camera/lens/exposure/raw tags might never have been extracted.

## Contract

- Persist an explicit metadata extraction/inspection contract version for each revision.
- Treat legacy inspection rows that predate versioning as an older version rather than as permanently current.
- Define one current extraction contract version in code; bump it intentionally when persisted metadata semantics or supported fields materially change.
- The normal metadata backfill candidate query selects revisions that are either uninspected **or stale** (`stored version < current version`).
- Re-inspection replaces the revision-bound structured/extended/raw metadata safely enough that a failed refresh does not leave a row falsely marked current.
- Preserve existing capture-time/GPS semantics and do not mutate manual Places, people, tags or other operator-controlled metadata.
- Keep the existing safety boundary: historical refresh only reads an already-local source whose size/SHA-256 still matches the immutable catalogue revision; it does not independently hydrate OneDrive content.
- Provide an explicit force/repair mode for re-reading even current-version rows when diagnosing parser changes or corrupted persisted metadata.
- Report counts that distinguish newly inspected, stale-version refreshed, forced-current refreshed, online-only deferred, changed and unavailable revisions.
- Document how operators can run the refresh in bounded batches for an existing catalogue.

## Implementation

`PhotoMetadataExtractionContract` defines legacy contract version `1` and current richer WI-0072 contract version `2`.

A separate `photo_metadata_inspections` table records the extraction-contract version completed for each immutable revision. The stable WI-0050 `photo_capture_metadata` table is not repurposed or widened for versioning. Existing capture rows with no version marker are therefore naturally interpreted as legacy version 1.

`PhotoMetadataInspectionService` writes in this order:

1. extended/rich metadata;
2. capture-time/GPS metadata;
3. the current extraction-version marker **last**.

If the process stops or a write fails before step 3, the revision remains missing/stale and is eligible for a later retry. Archive advancement's existing `IsInspectedAsync` guard now means **current extraction contract complete**, so a stale revision that the bounded archive workflow already has local can also be refreshed before continuing.

The normal `POST /api/photo-metadata/backfill` candidate query now includes:

- revisions with no capture-metadata row;
- revisions with a capture row but no inspection-version marker (legacy version 1);
- revisions whose stored version is below the current version.

Current-version rows are omitted by default. `force=true` deliberately selects them too for parser/repair diagnostics. Both modes retain the existing local-only, size/SHA-256 verified behavior and never request OneDrive hydration solely for metadata.

The response keeps total `Candidates`/`Persisted` and additionally reports `NewlyInspected`, `RefreshedStale`, `ForcedCurrentRefresh`, `CurrentContractVersion` and whether `Force` was requested.

## Operator use after merge

Normal historical upgrade (recommended):

```powershell
Invoke-RestMethod -Method Post `
  -Uri "http://127.0.0.1:5080/api/photo-metadata/backfill?limit=1000&offset=0"
```

This automatically includes older already-inspected rows whose extraction contract is stale. Do not delete existing metadata first.

Explicit repair of current-version local rows:

```powershell
Invoke-RestMethod -Method Post `
  -Uri "http://127.0.0.1:5080/api/photo-metadata/backfill?limit=1000&offset=0&force=true"
```

As before, online-only originals are deferred. To refresh those, make the desired OneDrive folder/files local through normal operator/OneDrive behavior and run bounded backfill again.

## Acceptance criteria

- [x] Existing pre-version metadata rows are recognized as stale by the implementation.
- [x] Default backfill processes both missing and stale metadata rows while skipping current-version rows.
- [x] A revision that already has capture date/GPS but lacks newer WI-0072 fields can be re-inspected and gains those fields when present in the original.
- [x] A successful refresh records the current extraction contract version.
- [x] Failed/deferred refresh cannot falsely mark a revision current because the version marker is written last.
- [x] Online-only originals remain deferred and no metadata-only hydration is requested.
- [x] Manual Place and other operator-controlled metadata are outside the refresh persistence path and remain untouched.
- [x] A force/repair path can intentionally refresh current rows without changing the default bounded behavior.
- [x] API/reporting and operator documentation distinguish new inspection from stale and forced-current refresh.
- [x] Focused integration tests cover legacy stale refresh, current-row skip, force refresh, version persistence and online-only deferral.
- [x] Final exact-head CI passed after retargeting to `main`: workflow #1207 (`32307001482`) on PR #195 head `d6b2d2ed205e83eeda407423bd0831c9b0944007`.

## Maintainer verification — 2026-08-21

The maintainer exercised normal bounded backfill, repeat/default behavior and `force=true` against the real catalogue and reported that WI-0078 works as expected. No corrective metadata-refresh implementation is requested.

A language-output issue observed in automatically derived Places is explicitly **not** a WI-0078 defect. It concerns GeoNames reverse-geocoding language selection and is tracked against WI-0064/WI-0065 in `../milestones/M20-maintainer-review-2026-08-21.md`.

## Non-goals

- Do not automatically download the entire historical OneDrive archive merely to update metadata.
- Do not use filesystem timestamps as substitutes for missing photographic capture metadata.
- Do not couple metadata refresh completion to GeoNames provider timing; GPS changes simply become eligible for the existing asynchronous enrichment worker.
