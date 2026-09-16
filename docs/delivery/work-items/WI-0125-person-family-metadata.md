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

- [ ] Person birth metadata supports exact and partial-date knowledge without inventing precision.
- [ ] Relationship records survive normal Person rename/merge semantics with deterministic conflict handling.
- [ ] Age-at-photo calculation handles missing/partial capture and birth dates explicitly.
- [ ] Creative Collection recipes can express at least one age-based and one relationship-based story.
- [ ] Automated tests cover partial dates, merges, relationship directionality and age boundary cases.

## Verification requirements

Automated domain/persistence/UI tests plus maintainer verification using private representative people and photos.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
