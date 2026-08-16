---
id: WI-0063
title: Make Places a first-class location hierarchy
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0050, WI-0056]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0063: Make Places a first-class location hierarchy

## Objective

Reserve the `Places/` hierarchy for location data, enforce one effective place per photo revision, and make hierarchical named places part of the Smart Collections **Location** dimension rather than ordinary tags.

## Place semantics

- `Places` is a reserved root, matched case-insensitively.
- A photo revision can have at most one effective place path.
- Canonical place vocabulary remains hierarchical and may reuse the existing canonical `photo_tags` path vocabulary so future Immich-compatible export remains straightforward.
- Parent nodes are reusable vocabulary, not separate assignments. A photo assigned `Places/Sweden/Stockholm region/Norrtälje` has one effective place, not four active tags.
- Setting a new place replaces the previous effective place. In particular, setting a more-specific descendant naturally replaces a less-specific ancestor.
- Manual place corrections take precedence over future automatically derived place values unless the maintainer explicitly chooses to replace them.

## In scope

- Prevent the generic manual-tag editor/API from treating `Places` or `Places/...` as ordinary tags.
- Add a dedicated Photo Details place editor that can set, replace and clear the effective place.
- Hide the literal `Places/` namespace prefix in normal UI labels.
- Persist effective place assignment separately from generic tag assignments, preferably with append-only `set`/`clear` audit history tied to the immutable revision.
- Retain canonical parent vocabulary for country/administrative/locality hierarchy.
- Exclude the `Places` subtree from the Smart Collections Tags selector and generic tag predicates.
- Extend the Smart Collections Location dimension with a canonical named-place filter in addition to optional GPS bounds.
- Implement hierarchical ancestor matching: selecting `Sweden` must match `Places/Sweden` and every descendant such as `Places/Sweden/Stockholm region/Norrtälje`; selecting `Norrtälje` resolves its full canonical node and matches that node and descendants.
- Avoid matching place-name segments globally by leaf text alone; selection must resolve a canonical hierarchy path so duplicate locality names in different countries remain distinct.
- Upgrade the saved Smart Collection filter schema to represent named-place location criteria without overloading the generic Tags array.
- Preserve compatibility for existing saved definitions and migrate coherent existing `Places/...` tag assignments into the new place model.
- Identify divergent legacy active Places assignments for explicit resolution rather than silently merging unrelated locations.
- Formalize the M19 lazy SQLite tables in a normal schema migration if practical while introducing the new place persistence structures.

## Smart Collection location contract

The Location dimension may contain:

- zero or one canonical place node; and
- optional GPS south/west/north/east bounds.

If both a place and GPS bounds are populated, both predicates must match. Different top-level Smart Collection dimensions continue to combine with AND semantics.

The UI should present a hierarchical place picker such as:

```text
Sweden
  Stockholm region
    Norrtälje
```

while storing the canonical internal value `Places/Sweden/Stockholm region/Norrtälje`.

## Migration expectations

- Existing canonical `Places/...` vocabulary should be retained rather than recreated under new IDs when possible.
- Existing active assignments forming one ancestor chain can migrate to the deepest effective path.
- Existing divergent active Places paths for one revision must be surfaced as a migration/review conflict so data is not silently discarded.
- Existing non-Places tags remain unchanged.

## Out of scope

- Reverse geocoding; that belongs to WI-0064.
- Downloading or embedding a geographic database.
- Map rendering or polygon-based administrative-boundary queries.
- Multiple simultaneous effective Places assignments for one photo.
- Renaming/merging the general tag vocabulary beyond migration needed for the reserved namespace.

## Acceptance criteria

- [ ] `Places` is reserved and cannot be assigned through the generic tag editor as an ordinary tag.
- [ ] A photo revision has at most one effective place, and setting another place atomically supersedes the previous value while retaining audit history.
- [ ] Photo Details can set, replace and clear the place without showing the `Places/` prefix in normal UI.
- [ ] `Places/Sweden/Stockholm region/Norrtälje` remains a canonical hierarchical value with reusable parent vocabulary.
- [ ] Smart Collections no longer show Places entries in the Tags dimension.
- [ ] Smart Collections expose named-place filtering in the Location dimension.
- [ ] Selecting an ancestor such as Sweden matches all descendant place assignments, while selecting Norrtälje resolves its full canonical hierarchy rather than matching unrelated leaf names.
- [ ] Named-place and GPS criteria can be combined safely with people, generic tags and taken time.
- [ ] Existing coherent Places assignments and saved definitions migrate without loss; divergent legacy assignments are surfaced for review.
- [ ] Place edits remain metadata-only and do not hydrate or modify originals.
- [ ] Automated tests cover reserved namespace enforcement, single-place replacement, ancestor matching, migration and Smart Collection persistence/query behavior.

## Verification requirements

Automated migration/persistence/API/query tests plus local verification of place replacement, hierarchical filtering and the separation between Tags and Location are required.
