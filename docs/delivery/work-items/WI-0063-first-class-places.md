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

## Implementation slices

### Slice 1 — first-class Places foundation

Merged PR #156 from `agent/WI-0063-places-foundation` establishes the storage/API boundary:

- `PhotoPlacePath` reserves the `Places` root while reusing the existing canonical hierarchical tag vocabulary and IDs.
- `photo_place_actions` provides append-only revision-level `set`/`clear` history; the newest revision action is the one effective place.
- dedicated place API routes expose vocabulary, per-photo state, manual set/replace/clear, and unresolved legacy migration conflicts.
- generic tag routes reject `Places`/`Places/...` and hide the reserved subtree from tag definitions and active tag responses.
- coherent legacy active Places chains migrate to their deepest effective node; divergent paths are recorded in `photo_place_migration_conflicts` for explicit resolution.
- explicit manual set/clear resolves an outstanding migration conflict and establishes the maintainer-owned state.
- place operations are catalogue-only and do not open or hydrate originals.

### Slice 2 — Smart Collection Location contract and formal schema migration

Draft PR #157 on `agent/WI-0063-smart-location` implements the non-UI Smart Collection semantics:

- Smart Collection filter schema v2 adds one optional canonical named place inside Location while preserving optional GPS bounds and coordinate-only request compatibility.
- named places normalize to their full internal `places/...` path while API responses omit the literal `Places/` prefix.
- named-place matching uses exact canonical ancestry (`assigned = selected` or the assigned path begins with `selected + '/'`) implemented without SQL wildcard matching.
- the Places subtree is excluded from generic Smart Collection tag predicates and new API requests reject Places values in Tags.
- a representable legacy saved definition containing one Places tag migrates that criterion into Location on read; legacy `tagMatch: any` filters that mix a Places tag with generic tags are rejected because converting them to separate Location and Tags dimensions would silently change OR semantics to AND.
- SQLite schema v14 formalizes the M19 lazy `photo_capture_metadata`, `smart_collections`, `photo_person_actions`, `photo_place_actions` and place-conflict structures.
- schema v14 rebuilds `smart_collections` with filter schema v2 and promotes v1 rows without rewriting their compatible JSON payloads.
- automated coverage combines named place + GPS + people + generic tags + taken date and verifies hierarchy ancestry, duplicate locality names, API reservation, lossless legacy-filter handling and a simulated v13/v1 migration.

### Slice 3 — Photo Details and Smart Collection UI

- add a dedicated Photo Details place editor with set/replace/clear and migration-conflict presentation;
- add hierarchical named-place selection to the Smart Collections Location editor while hiding the literal `Places/` prefix;
- preserve saved/transient Smart Collection navigation state from WI-0061;
- complete automated browser-contract coverage and defer the maintainer browser/operator pass to the consolidated M19 verification.

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
- Formalize the M19 lazy SQLite tables in a normal schema migration while introducing the Smart Collection v2 contract.

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
- Existing v1 saved Smart Collections keep their GPS bounds, people, tags and taken-time criteria when promoted to filter schema v2.
- A legacy saved definition with one Places tag migrates that tag into the named-place Location criterion when its boolean semantics are representable in v2; ambiguous multi-place filters and `tagMatch: any` filters that mix Places with generic tags are rejected rather than silently collapsed or changed.

## Out of scope

- Reverse geocoding; that belongs to WI-0064.
- Downloading or embedding a geographic database.
- Map rendering or polygon-based administrative-boundary queries.
- Multiple simultaneous effective Places assignments for one photo.
- Renaming/merging the general tag vocabulary beyond migration needed for the reserved namespace.

## Acceptance criteria

- [x] `Places` is reserved and cannot be assigned through the generic tag API as an ordinary tag. Browser editor verification remains for Slice 3/consolidated M19 review.
- [x] A photo revision has at most one effective place, and setting another place atomically supersedes the previous value while retaining audit history.
- [ ] Photo Details can set, replace and clear the place without showing the `Places/` prefix in normal UI. Persistence/API complete in Slice 1; UI remains Slice 3.
- [x] `Places/Sweden/Stockholm region/Norrtälje` remains a canonical hierarchical value with reusable parent vocabulary.
- [ ] Smart Collections no longer show Places entries in the Tags dimension. Backend predicates/API exclusion are complete in Slice 2; browser selector remains Slice 3.
- [ ] Smart Collections expose named-place filtering in the Location dimension. Persistence/query/API complete in Slice 2; hierarchical browser selector remains Slice 3.
- [x] Selecting an ancestor such as Sweden matches all descendant place assignments, while selecting Norrtälje resolves its full canonical hierarchy rather than matching unrelated leaf names.
- [x] Named-place and GPS criteria can be combined safely with people, generic tags and taken time.
- [x] Existing coherent Places assignments and representable saved definitions migrate without loss; divergent or non-representable legacy cases are surfaced/rejected rather than silently changing semantics. Slice 1 covers photo-assignment migration/conflicts and Slice 2 covers saved-filter v1→v2 migration.
- [x] Place edits remain metadata-only and do not hydrate or modify originals.
- [x] Automated tests cover reserved namespace enforcement, single-place replacement, ancestor matching, migration and Smart Collection persistence/query behavior. Browser/operator verification remains deferred.

## Verification requirements

Automated migration/persistence/API/query tests plus local verification of place replacement, hierarchical filtering and the separation between Tags and Location are required. Per the maintainer's M19 plan, local browser/operator verification is intentionally deferred until WI-0063 and WI-0064 implementation are complete so M19 can be reviewed as one integrated workflow.
