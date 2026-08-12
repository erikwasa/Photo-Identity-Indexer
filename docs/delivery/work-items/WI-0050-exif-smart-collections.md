---
id: WI-0050
title: Add EXIF metadata and smart collections
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0025, WI-0041, WI-0042, WI-0056]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Source.Local, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0050: Add EXIF metadata and smart collections

## Objective

Ingest photographic capture metadata and let the maintainer save reusable collections based on capture time, location, people and canonical photo tags.

## Why

The existing collection engine is identity-oriented and its stored observation timestamp describes catalogue ingestion rather than when the photo was taken. Library use needs photographic time/location semantics, canonical tag predicates and reusable queries.

## In scope

- Extract and persist EXIF DateTimeOriginal/capture time where available.
- Treat timezone-less camera timestamps as local photographic wall-clock time rather than inventing UTC precision.
- Preserve EXIF offset/timezone information separately when the source actually provides it.
- Extract and persist GPS latitude/longitude when present.
- Keep metadata associated with immutable asset revisions and source provenance.
- Extend collection queries and UI to filter by capture date/time and geographic criteria.
- Persist named smart-collection definitions that reevaluate against the current catalogue rather than copying a fixed list of asset IDs.
- Combine metadata predicates with existing people predicates.
- Include predicates over the canonical tag representation established by WI-0056. Manual tags must work regardless of whether WI-0049 selects a production automatic model.
- If production automatic tag evidence exists when this item is implemented, define an explicit query policy for whether/how that advisory evidence can qualify independently of manual assignments; do not silently treat model evidence as human-assigned tags.
- Define fallback behavior for photos with missing or malformed EXIF.

## Out of scope

- Geocoding coordinates into place names unless separately selected.
- Treating catalogue observation time as a substitute for missing photographic capture time.
- Blocking manual tag predicates on WI-0049 experimentation.
- Writing metadata back to original photo files.

## Acceptance criteria

- [ ] Capture timestamps preserve the source's local/offset semantics without false UTC conversion.
- [ ] GPS metadata is available for collection filtering when present and optional when absent.
- [ ] Existing assets can have metadata populated without changing canonical asset/revision identity.
- [ ] Smart collections can be saved, reopened and reevaluated after new matching photos enter the catalogue.
- [ ] Smart collections can combine people with capture-date/location predicates.
- [ ] Smart collections can filter on canonical manual tags from WI-0056.
- [ ] Any automatic tag-evidence predicate has an explicit provenance/threshold policy rather than being conflated with manual assignment.

## Verification requirements

Automated metadata parsing/query tests using non-private fixtures plus human verification against representative real-camera EXIF, canonical tags and saved collection behavior.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
