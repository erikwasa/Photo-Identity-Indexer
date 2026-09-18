---
id: WI-0144
title: Allow Smart Collections to match any of multiple named locations
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0063, WI-0140]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, PhotoIdentity.Persistence.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0144: Allow Smart Collections to match any of multiple named locations

## Objective

Allow selecting multiple named Places in one Smart Collection, where a photo matches any selected hierarchy.

## Why

The current filter stores one LocationPlace, preventing straightforward Stockholm OR Norrtälje queries while other dimensions support multi-selection.

## In scope

- Evolve the filter schema to a bounded collection of named places.
- Use ANY semantics while preserving descendant matching.
- Read existing definitions as single-element lists.
- Use searchable multi-selection from WI-0140.
- Preserve AND semantics between named places and optional GPS rectangle.

## Out of scope

- Arbitrary boolean expression builders.
- Multiple GPS rectangles.

## Acceptance criteria

- [ ] Two or more Places match any selected hierarchy.
- [ ] Existing single-place collections retain results.
- [ ] Ancestor/duplicate selections normalize predictably.
- [ ] PostgreSQL queries remain bounded.
- [ ] UI communicates OR semantics without a rule builder.

## Verification requirements

Core/schema compatibility, PostgreSQL query tests and UI verification.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
