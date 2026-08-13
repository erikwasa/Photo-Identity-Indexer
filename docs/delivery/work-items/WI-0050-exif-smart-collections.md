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

The existing collection engine is identity-oriented and its stored observation timestamp describes catalogue ingestion rather than when the photo was taken. Library use needs photographic time/location semantics, canonical tag predicates and reusable queries. Automatic tagging is intended to supply the normal tag coverage; manual tags exist as fallback/correction when automation needs human intervention.

## In scope

- Extract and persist EXIF DateTimeOriginal/capture time where available.
- Treat timezone-less camera timestamps as local photographic wall-clock time rather than inventing UTC precision.
- Preserve EXIF offset/timezone information separately when the source actually provides it.
- Extract and persist GPS latitude/longitude when present.
- Keep metadata associated with immutable asset revisions and source provenance.
- Extend collection queries and UI to filter by capture date/time and geographic criteria.
- Persist named smart-collection definitions that reevaluate against the current catalogue rather than copying a fixed list of asset IDs.
- Combine metadata predicates with existing people predicates.
- Include predicates over the canonical tag representation established by WI-0056 without hard-coding manual assignments as the primary tag source.
- Define tag-query semantics around an explicit effective-tag policy: automatic output is the normal source once the production automatic pipeline exists, while explicit manual fallback/correction can take precedence for a conflicting tag without destroying model provenance.
- Allow manual fallback tags to remain queryable when automatic tagging is unavailable for a particular photo or concept.
- Define fallback behavior for photos with missing or malformed EXIF.

## Out of scope

- Geocoding coordinates into place names unless separately selected.
- Treating catalogue observation time as a substitute for missing photographic capture time.
- Treating manual-only tagging as the intended steady-state tag source for M19.
- Writing metadata back to original photo files.

## Acceptance criteria

- [ ] Capture timestamps preserve the source's local/offset semantics without false UTC conversion.
- [ ] GPS metadata is available for collection filtering when present and optional when absent.
- [ ] Existing assets can have metadata populated without changing canonical asset/revision identity.
- [ ] Smart collections can be saved, reopened and reevaluated after new matching photos enter the catalogue.
- [ ] Smart collections can combine people with capture-date/location predicates.
- [ ] Smart collections can filter on canonical effective tags without assuming manual assignments are the normal source.
- [ ] Manual fallback/correction tags remain usable when automatic tagging does not provide the needed tag.
- [ ] Any automatic tag-evidence predicate or effective-tag rule has explicit provenance/threshold/override semantics rather than being conflated with manual intervention.

## Verification requirements

Automated metadata parsing/query tests using non-private fixtures plus human verification against representative real-camera EXIF, canonical tags and saved collection behavior.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
