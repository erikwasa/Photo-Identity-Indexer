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

The Slice 2 query contract is reused directly by saved definitions:

- `people`: canonical person IDs plus `peopleMatch=all|any`;
- `tags`: canonical hierarchical full values plus `tagMatch=all|any`;
- optional GPS rectangle (`south`, `west`, `north`, `east`);
- optional taken-date shorthand normalized through the documented date parser;
- zero populated people is valid, so tag-only, location-only and taken-time-only collections are first-class;
- populated dimensions combine with AND semantics;
- missing capture metadata cannot satisfy location or taken-time predicates.

## Saved-definition contract

- A saved collection has a stable generated ID, canonical display name, normalized case-insensitive name identity and created/updated timestamps.
- The stored payload is a versioned canonical filter definition. People IDs and hierarchical tag values are normalized and taken-date shorthand is persisted as explicit inclusive `from`/`to` dates.
- No asset/revision membership rows are persisted.
- Create, list, get, update and delete operate on the definition only.
- Evaluating `/api/smart-collections/{id}/query` loads the saved filter and executes the same current-catalogue query implementation used by the transient Slice 2 endpoint.

## Saved-collection UI contract

- `/smart-collections` is a dedicated local web workspace linked from primary navigation; the existing `/collections` people/evidence workspace remains unchanged.
- The editor loads canonical people and WI-0056 tag vocabulary from the existing APIs.
- People and tags expose explicit `all`/`any` selection modes.
- Taken time accepts the documented shorthand; reopening a saved definition reconstructs an editable expression from the persisted explicit bounds.
- Location can be enabled as an optional south/west/north/east GPS rectangle.
- The current editor can be previewed without saving; saved definitions can be reopened, edited, deleted and explicitly reevaluated.
- Result pages use the same current-catalogue smart query API and link each matching revision back to its photo detail route.

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
- [x] Metadata inspection does not hydrate online-only originals.
- [x] Saved smart collections can be created, reopened, edited and deleted through the web workspace.
- [x] A saved collection reevaluates against the current catalogue and includes newly matching photos automatically.
- [x] People, tags, location and taken time work independently and can all be combined in one collection.
- [x] People and tags each support explicit `all` and `any` matching.
- [x] The three documented date-input examples normalize to correct inclusive bounds.
- [x] Tag predicates use WI-0056 hierarchical full values.
- [x] Missing data never fabricates a match.

## Implementation status

- Slice 1 merged in PR #143 with successful workflow `31756173422`. It established capture-time/GPS parsing, revision-bound persistence and bounded backfill candidates.
- Slice 2 merged in PR #144 with successful workflow `31760294369`. It added explicit local-only verified metadata backfill plus the combined smart-collection filter/query contract.
- Slice 3 merged in PR #145 with successful workflow `31800391197`. It persisted canonical saved definitions and added create/list/get/update/delete plus saved-query API operations.
- Slice 4 merged in PR #146 with successful workflow `31802548161`. It added the `/smart-collections` saved-definition workspace without changing the legacy `/collections` flow.
- The maintainer completed the integrated M19 baseline verification on 2026-08-16 and reported that M19 and the work-item functions behaved as expected.
- WI-0050 is complete. Later M19 additions are tracked separately in WI-0061 through WI-0064 so the verified baseline contract remains stable and auditable.

## Completed slices

1. Capture-time/GPS persistence and bounded backfill foundation — PR #143.
2. Safe explicit metadata backfill execution plus combined collection-filter/query contract — PR #144.
3. Persisted smart-collection CRUD/query API — PR #145.
4. Saved-collection UI — PR #146.
5. Integrated maintainer verification — completed 2026-08-16.
