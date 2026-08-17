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

PR #156 merged as `2c67f7ae653c441b6ce11e79e89d3f9c38d7aef2` and established the storage/API boundary:

- `PhotoPlacePath` reserves the `Places` root while reusing the existing canonical hierarchical tag vocabulary and IDs.
- `photo_place_actions` provides append-only revision-level `set`/`clear` history; the newest revision action is the one effective place.
- dedicated place API routes expose vocabulary, per-photo state, manual set/replace/clear, and unresolved legacy migration conflicts.
- generic tag routes reject `Places`/`Places/...` and hide the reserved subtree from tag definitions and active tag responses.
- coherent legacy active Places chains migrate to their deepest effective node; divergent paths are recorded in `photo_place_migration_conflicts` for explicit resolution.
- explicit manual set/clear resolves an outstanding migration conflict and establishes the maintainer-owned state.
- place operations are catalogue-only and do not open or hydrate originals.

### Slice 2 — Smart Collection Location contract

Draft PR #158 on `agent/WI-0063-smart-place-location` implements the query/persistence contract:

- Smart Collection filter schema v2 adds an optional canonical named place inside Location while retaining optional GPS bounds.
- v1 saved rows remain readable through the compatibility deserializer; new/edited definitions persist as v2.
- the lazy `smart_collections` table is rebuilt in place when necessary so its schema constraint accepts versions 1 and 2 without rewriting existing filter JSON.
- `Places` is excluded from generic Smart Collection tag normalization and query predicates.
- named-place filtering compares the selected canonical hierarchy path against the effective place path using exact-or-descendant semantics only.
- duplicate locality leaf names in unrelated hierarchies therefore remain distinct.
- named place and GPS bounds combine with AND semantics and continue composing with people, generic tags and taken time.
- integration coverage exercises ancestor matching, duplicate locality names, GPS + place composition, generic Places exclusion, and v1/v2 saved-definition compatibility.

The catalogue-wide `PRAGMA user_version` marker is not changed in this PR because the connected GitHub contents API currently permits only whole-file replacement for the large catalogue bootstrap file. The persisted structures remain startup-safe/idempotent and correctness does not depend on that marker. The lazy M19 tables should still be folded into the normal catalogue migration when a safe patch path for that bootstrap file is available.

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
- Formalize the M19 lazy SQLite tables in a normal schema migration when the catalogue bootstrap can be safely patch-ed.

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
- Existing saved Smart Collection v1 definitions remain readable; editing one upgrades that definition to v2.

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
- [x] Smart Collection generic tag normalization/query predicates exclude the Places subtree. Browser selector verification remains Slice 3.
- [x] Smart Collections expose named-place filtering in the Location API/domain contract. Browser picker remains Slice 3.
- [x] Selecting an ancestor such as Sweden matches all descendant place assignments, while selecting a canonical Stockholm path does not match an unrelated Stockholm hierarchy.
- [x] Named-place and GPS criteria can be combined safely; existing people/generic-tag/taken semantics remain independent AND dimensions.
- [x] Existing coherent Places assignments and v1 saved definitions migrate/read without loss; divergent legacy assignments are surfaced for review.
- [x] Place edits remain metadata-only and do not hydrate or modify originals.
- [x] Automated tests cover reserved namespace enforcement, single-place replacement, ancestor matching, legacy migration and Smart Collection persistence/query behavior across Slices 1–2.

## Verification requirements

Automated migration/persistence/API/query tests plus local verification of place replacement, hierarchical filtering and the separation between Tags and Location are required. Per the maintainer's M19 plan, local browser/operator verification is intentionally deferred until WI-0063 and WI-0064 implementation are complete so M19 can be reviewed as one integrated workflow.
