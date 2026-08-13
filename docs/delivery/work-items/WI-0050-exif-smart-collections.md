---
id: WI-0050
title: Add photo metadata and persistent smart collections
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0025, WI-0041, WI-0042, WI-0056]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Source.Local, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0050: Add photo metadata and persistent smart collections

## Objective

Persist photographic capture metadata and named smart-collection definitions that dynamically query the current catalogue by people, hierarchical tags, location and taken time.

Automatic visible-content tagging is on hold. Manual hierarchical tags from WI-0056 are the tag source for this work.

## Filter contract

- People: zero or more canonical people, with `all` or `any` matching.
- Tags: zero or more canonical full tag values, with `all` or `any` matching.
- Location: optional GPS criteria; coordinate bounds are sufficient initially and reverse geocoding is not required.
- Taken time: optional inclusive photographic date bounds.
- Populated dimensions combine with **AND** semantics.

Date input should accept convenient forms including `2016`, `2020-2021` and `2025/05/01-2025/05/10`, then persist normalized explicit start/end dates.

A saved smart collection stores its filter definition, not a copied list of asset IDs. Evaluating it later must include newly ingested photos that now match the same definition.

## In scope

- Persist EXIF capture time without inventing UTC for timezone-less camera timestamps; preserve a real source offset separately when present.
- Persist GPS latitude/longitude when present.
- Backfill metadata for existing revisions without changing canonical asset/revision identity.
- Generalize collection queries so people are optional and can combine with tag, GPS and taken-time predicates.
- Use photographic capture time rather than catalogue observation time for taken-time filters.
- Persist smart-collection definitions in SQLite with create/list/get/update/delete and query operations.
- Add UI to create, edit, reopen and evaluate saved collections.
- Treat missing metadata as a non-match for a populated predicate.

## Out of scope

Automatic tagging, reverse geocoding, sidecar/original metadata write-back, static copied membership lists and substituting catalogue observation time for missing capture time.

## Acceptance criteria

- [ ] Capture time and GPS metadata are persisted with correct source semantics.
- [ ] Saved smart collections can be created, reopened, edited and deleted.
- [ ] A saved collection reevaluates against the current catalogue and includes newly matching photos automatically.
- [ ] People, tags, location and taken time work independently and can all be combined in one collection.
- [ ] People and tags each support explicit `all` and `any` matching.
- [ ] The three documented date-input examples normalize to correct inclusive bounds.
- [ ] Tag predicates use WI-0056 hierarchical full values.
- [ ] Missing data never fabricates a match.

## Planned slices

1. Capture-time/GPS persistence and backfill.
2. Combined collection-filter/query contract.
3. Persisted smart-collection CRUD/query API.
4. Saved-collection UI.
5. Maintainer verification.
