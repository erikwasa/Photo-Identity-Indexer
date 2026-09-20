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

- [x] Two or more Places match any selected hierarchy.
- [x] Existing single-place collections retain results.
- [x] Ancestor/duplicate selections normalize predictably.
- [x] PostgreSQL queries remain bounded.
- [x] UI communicates OR semantics without a rule builder.

## Verification requirements

Core/schema compatibility, PostgreSQL query tests and UI verification.

## Completion notes

- Files changed: core multi-place normalization, Smart Collection filter schema v3 persistence and migrations, PostgreSQL/SQLite query adapters, API/web contracts, searchable multi-selection UI, transient navigation state, compatibility/query tests and delivery tracking.
- Trade-offs: named-place selections are bounded to 16 normalized hierarchies. Duplicate paths are removed and descendants collapse under a selected ancestor before the bound is applied. Named places use ANY semantics internally while the optional GPS rectangle remains an independent AND criterion. The legacy single `Place` API and v1/v2 persisted definitions remain readable; new/updated definitions use filter schema v3.
- Deferred work: desktop/phone interaction verification remains bundled with the later M28 verification pass. Arbitrary boolean location builders and multiple GPS rectangles remain out of scope.
- Commands run: implementation prepared through the GitHub connector; PR #398 runs the normal build, integration, documentation, package and PostgreSQL verification gates.
