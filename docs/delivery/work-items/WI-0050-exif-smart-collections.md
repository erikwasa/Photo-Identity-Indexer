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

Date input accepts convenient forms including `2016`, `2020-2021` and `2025/05/01-2025/05/10`, then normalizes them to explicit inclusive start/end dates.

A saved smart collection stores its filter definition, not a copied list of asset IDs. Evaluating it later must include newly ingested photos that now match the same definition.

## Capture-metadata contract

- `DateTimeOriginal` is stored as photographic wall-clock time. A timezone-less camera timestamp is not converted to UTC.
- A real EXIF original-time offset is stored separately when present.
- GPS latitude/longitude are optional but atomic: both coordinates are stored together or neither is stored.
- Capture metadata is revision-bound and does not replace catalogue `observed_at_utc`.
- A persisted empty metadata record means the revision was inspected and had no usable capture-time/GPS values. No record means it is still eligible for backfill.
- Backfill candidates retain the expected immutable revision content hash so metadata is not attached to the wrong revision if the source file has changed.
- Metadata backfill checks Files On-Demand state before opening the source and only reads files already reported `Local`; it never requests hydration.
- Backfill is explicitly triggered as a bounded `POST /api/photo-metadata/backfill` operation rather than an always-on background reader, so metadata inspection does not compete with viewer requests.
- Deferred online-only candidates remain eligible for a later retry and the operation accepts paging so they cannot starve later local candidates.
- Originals and sidecars remain read-only.

## Combined query contract

The Slice 2 query contract is reusable by the persisted-definition API planned for Slice 3:

- `people`: canonical person IDs plus `peopleMatch=all|any`;
- `tags`: canonical hierarchical full values plus `tagMatch=all|any`;
- optional GPS rectangle (`south`, `west`, `north`, `east`);
- optional taken-date shorthand normalized through the documented date parser;
- zero populated people is valid, so tag-only, location-only and taken-time-only collections are first-class;
- populated dimensions combine with AND semantics;
- missing capture metadata cannot satisfy location or taken-time predicates.

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

- [x] Capture time and GPS metadata are persisted with correct source semantics.
- [x] Existing revisions can be identified for bounded metadata backfill without changing their canonical identity.
- [ ] Metadata inspection does not hydrate online-only originals.
- [ ] Saved smart collections can be created, reopened, edited and deleted.
- [ ] A saved collection reevaluates against the current catalogue and includes newly matching photos automatically.
- [ ] People, tags, location and taken time work independently and can all be combined in one collection.
- [ ] People and tags each support explicit `all` and `any` matching.
- [ ] The three documented date-input examples normalize to correct inclusive bounds.
- [ ] Tag predicates use WI-0056 hierarchical full values.
- [ ] Missing data never fabricates a match.

## Implementation status

- Slice 1 merged in PR #143 with successful workflow `31756173422`. It established capture-time/GPS parsing, revision-bound persistence and bounded backfill candidates.
- Slice 2 is active on `agent/WI-0050-backfill-query-slice2`. It adds explicit local-only verified backfill execution plus the combined smart-collection filter/query contract.
- Slice 3 will persist the normalized filter contract and expose saved smart-collection CRUD/query operations.
- Maintainer verification remains one integrated pass after all non-deferred M19 implementation is complete.

## Planned slices

1. Capture-time/GPS persistence and bounded backfill foundation — merged in PR #143.
2. Safe explicit metadata backfill execution plus combined collection-filter/query contract — current.
3. Persisted smart-collection CRUD/query API.
4. Saved-collection UI.
5. One maintainer verification pass for the complete non-deferred M19 scope.
