---
id: WI-0125
title: Add birth dates and family relationships for age-aware Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0121, WI-0060]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0125: Add birth dates and family relationships for age-aware Creative Collections

## Objective

Extend stable Person metadata with optional birth-date precision and family relationships so Creative Collections can express age- and relationship-oriented stories.

## Why

For a family archive, age and relationship context can unlock high-value collections such as a child at age five, grandparents with grandchildren or several generations together without depending on travel/location metadata.

## In scope

- Define optional person birth information with explicit precision where only a year or year/month is known.
- Define operator-maintained directed/symmetric family relationship types with stable PersonIds and clear merge behavior.
- Support parent/child, spouse, sibling, grandparent/grandchild and cousin relationships. Cousin is symmetric.
- Present recorded relationships in the People maintenance UI grouped by relationship category so multiple children, parents, siblings, cousins, etc. read as one grouped section rather than one repeated label per row.
- Keep the relationship list wide/responsive enough that related-person names and the Remove action remain readable on desktop and phone.
- Derive age-at-photo from person metadata plus capture date rather than storing redundant per-photo age facts unless measurement justifies it.
- Expose recipe/filter/selection primitives for age ranges and selected relationship categories.
- Preserve manual provenance and avoid inferring relationships automatically from face co-occurrence.

## Out of scope

- Genealogy import/synchronization.
- Automatic kinship inference from facial appearance.
- Public family-tree sharing.

## Acceptance criteria

- [x] Person birth metadata supports exact and partial-date knowledge without inventing precision.
- [x] Relationship records survive normal Person rename/merge semantics with deterministic conflict handling.
- [x] Age-at-photo calculation handles missing/partial capture and birth dates explicitly.
- [x] Creative Collection anchors can express at least one age-based and one relationship-based story through saved Smart Collection criteria.
- [x] Cousin is supported end-to-end as a symmetric relationship in People maintenance, persistence and Smart Collection relationship filters.
- [x] People maintenance groups multiple relatives under one relationship heading and uses a responsive width where related names and Remove controls remain usable.
- [ ] Automated tests cover partial dates, merges, relationship directionality/symmetry, cousin behavior and age boundary cases and pass in CI/live PostgreSQL verification.

## Verification requirements

Automated domain/persistence/UI tests plus maintainer verification using private representative people and photos.

For manual verification, create or choose three known people, enter at least one year-only or month-only birth date, add a directed family relationship, reopen both people and confirm the inverse relationship is understandable. Add multiple people in one category and confirm they are grouped under a single heading. Add a cousin relationship and confirm it is symmetric from either person's perspective. Then create saved Smart Collection criteria for a known age range and a known relationship story and confirm Creative preview uses the resulting exact anchor set without changing Person or photo metadata.

## Maintainer review — 2026-09-25

The maintainer reported that birth metadata, age filtering, the implemented relationship directions, saved/reopened family-aware Smart Collections and Creative anchor behavior work on representative private data. The remaining requested gaps were cousin support and refinement of the People relationship presentation: repeated relationships should be grouped by category, the relation column should be wider, and the Remove action must fit/read cleanly.

The first `verify-postgres.ps1` run exposed a reversed relationship expectation in `PostgresSmartCollectionFamilyFilterTests`; PR #418 corrected the test to use relationship kinds from the focal person's perspective and added explicit inverse-direction coverage. The production relationship semantics were unchanged.

## Completion notes

- Files changed: added a Person family-metadata domain model and age-range calculator; PostgreSQL birth/relationship persistence with merge rewiring; People maintenance API/UI; saved Smart Collection age/relationship filter persistence/query support; domain and live-PostgreSQL tests.
- Smart Collection UI follow-up: age-at-photo and family-relationship criteria are editable in the Smart Collections workspace, summarized on saved collection cards, restored from saved definitions and preserved in transient browser-tab previews. The UI validates the 0-130 age range, focal-person selection and at least one relationship kind before sending a query or definition.
- Cousin follow-up: `cousin` is now a first-class symmetric relationship. It normalizes/inverts to itself, is canonicalized like spouse/sibling, survives merge rewiring, upgrades the existing PostgreSQL relationship-kind constraint/index in place, and is available in People maintenance plus Smart Collection relationship filters.
- People UI follow-up: existing relationships are grouped once per category, related people are sorted inside the group, the relationship card gets a wider desktop share, add controls remain responsive, and each Remove action has non-wrapping space reserved so its label stays usable. The layout collapses back to one column on narrow screens.
- Trade-offs: partial dates remain inclusive ranges. Age filtering matches when the possible age range overlaps the requested range, rather than pretending year/month precision is exact. Directed relationships are stored canonically as parent/grandparent edges and exposed from either person's perspective with deterministic inverse semantics. Symmetric spouse/sibling/cousin edges use canonical person ordering to avoid duplicates.
- Merge behavior: existing survivor birth metadata wins; otherwise source birth metadata moves to the survivor. Relationships are rewired to the surviving PersonId, duplicate edges collapse and self-relationships are discarded.
- Creative boundary: the new criteria extend exact saved Smart Collection anchors. Creative Collection selection itself remains unchanged and consumes the resulting anchor set through the existing WI-0121/WI-0120 path.
- Persistence note: the family schema is included in fresh PostgreSQL catalogue initialization through the repository's existing current-migration schema-extension pattern, and the repository retains the same idempotent schema guard for catalogues already initialized by an earlier schema-v31 build. The cousin follow-up upgrades existing relationship constraints/indexes idempotently rather than requiring a schema-version bump.
- Fresh/live coverage verifies family schema bootstrap, partial birth dates, directed inverse semantics, cousin symmetry/deduplication, merge rewiring and Smart Collection family filtering.
- Implementation PRs before this follow-up: #415, #416 and verification correction #418. WI-0125 remains `in_progress` until this final follow-up passes CI, `verify-postgres.ps1` and maintainer UI verification.
