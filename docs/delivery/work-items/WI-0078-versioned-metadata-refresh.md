---
id: WI-0078
title: Reprocess stale photo metadata after extraction-contract changes
milestone: M20
status_source: ../status/work-items.yaml
depends_on: [WI-0072]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Source.Local, documentation]
---

# WI-0078: Reprocess stale photo metadata after extraction-contract changes

## Objective

Ensure catalogue revisions that were inspected by an older metadata reader are automatically eligible for bounded re-inspection when Photo Identity adds or changes supported metadata fields.

The current backfill selector only chooses revisions with no `photo_capture_metadata` row. A revision inspected before WI-0072 therefore looks complete even though newer fields such as camera/lens/exposure/raw tags may never have been extracted.

## Contract

- Persist an explicit metadata extraction/inspection contract version for each revision.
- Treat legacy inspection rows that predate versioning as an older version rather than as permanently current.
- Define one current extraction contract version in code; bump it intentionally when persisted metadata semantics or supported fields materially change.
- The normal metadata backfill candidate query selects revisions that are either uninspected **or stale** (`stored version < current version`).
- Re-inspection replaces the revision-bound structured/extended/raw metadata atomically enough that a failed refresh does not leave a row falsely marked current.
- Preserve existing capture-time/GPS semantics and do not mutate manual Places, people, tags or other operator-controlled metadata.
- Keep the existing safety boundary: historical refresh only reads an already-local source whose size/SHA-256 still matches the immutable catalogue revision; it does not independently hydrate OneDrive content.
- Provide an explicit force/repair mode for re-reading even current-version rows when diagnosing parser changes or corrupted persisted metadata.
- Report counts that distinguish newly inspected, stale-version refreshed, current/skipped, online-only deferred, changed and unavailable revisions.
- Document how operators can run the refresh in bounded batches for an existing catalogue.

## Migration strategy

A practical first migration is:

1. add an `extraction_contract_version` (or equivalent) to the durable metadata inspection marker;
2. treat existing rows without a version as legacy version `1`;
3. define the richer WI-0072 reader contract as the next current version;
4. allow `/api/photo-metadata/backfill` to select legacy/stale rows as well as missing rows;
5. only write the current version after the complete structured + extended/raw metadata save succeeds.

This lets existing JPEG/HEIC revisions acquire newly supported fields without deleting metadata rows manually or pretending they were never inspected.

## Acceptance criteria

- [ ] Existing pre-version metadata rows are recognized as stale after migration.
- [ ] Default backfill processes both missing and stale metadata rows while skipping current-version rows.
- [ ] A revision that already has capture date/GPS but lacks newer WI-0072 fields can be re-inspected and gains those fields when present in the original.
- [ ] A successful refresh records the current extraction contract version.
- [ ] Failed/deferred refresh does not falsely mark a revision current.
- [ ] Online-only originals remain deferred and no metadata-only hydration is requested.
- [ ] Manual Place and other operator-controlled metadata are untouched.
- [ ] A force/repair path can intentionally refresh current rows without changing the default bounded behavior.
- [ ] API/reporting and operator documentation clearly distinguish new inspection from stale refresh.
- [ ] Tests cover legacy row migration, stale refresh, current-row skip, force refresh and online-only deferral.

## Non-goals

- Do not automatically download the entire historical OneDrive archive merely to update metadata.
- Do not use filesystem timestamps as substitutes for missing photographic capture metadata.
- Do not couple metadata refresh completion to GeoNames provider timing; GPS changes simply become eligible for the existing asynchronous enrichment worker.
