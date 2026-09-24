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
- [ ] Automated tests cover partial dates, merges, relationship directionality and age boundary cases and pass in CI/live PostgreSQL verification.

## Verification requirements

Automated domain/persistence/UI tests plus maintainer verification using private representative people and photos.

For manual verification, create or choose three known people, enter at least one year-only or month-only birth date, add a directed family relationship, reopen both people and confirm the inverse relationship is understandable. Then create saved Smart Collection criteria for a known age range and a known relationship story and confirm Creative preview uses the resulting exact anchor set without changing Person or photo metadata.

## Completion notes

- Files changed: added a Person family-metadata domain model and age-range calculator; PostgreSQL birth/relationship persistence with merge rewiring; People maintenance API/UI; saved Smart Collection age/relationship filter persistence/query support; domain and live-PostgreSQL tests.
- Smart Collection UI follow-up: age-at-photo and family-relationship criteria are now editable in the Smart Collections workspace, summarized on saved collection cards, restored from saved definitions and preserved in transient browser-tab previews. The UI validates the 0-130 age range, focal-person selection and at least one relationship kind before sending a query or definition.
- Trade-offs: partial dates remain inclusive ranges. Age filtering matches when the possible age range overlaps the requested range, rather than pretending year/month precision is exact. Relationships are stored canonically as parent/spouse/sibling/grandparent edges and exposed from either person's perspective with deterministic inverse semantics. Symmetric edges are canonicalized to avoid duplicate spouse/sibling rows.
- Merge behavior: existing survivor birth metadata wins; otherwise source birth metadata moves to the survivor. Relationships are rewired to the surviving PersonId, duplicate edges collapse and self-relationships are discarded.
- Creative boundary: the new criteria extend exact saved Smart Collection anchors. Creative Collection selection itself remains unchanged and consumes the resulting anchor set through the existing WI-0121/WI-0120 path.
- Persistence note: the family schema is included in fresh PostgreSQL catalogue initialization through the repository's existing current-migration schema-extension pattern, and the repository retains the same idempotent schema guard for catalogues already initialized by an earlier schema-v31 build. Both paths use one shared SQL definition to avoid bootstrap/repository drift. No SQLite compatibility implementation is added because M29 already schedules retirement of SQLite runtime support.
- Fresh-bootstrap coverage verifies that catalogue initialization alone creates the birth table, relationship table and merge trigger before any family repository call, and that repeated initialization remains safe.
- Commands run: implementation and review prepared through the GitHub connector; PR CI plus `verify-postgres.ps1` are the required execution gates before completion.
