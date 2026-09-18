---
id: WI-0145
title: Add explicit photo-list collections for manually assembled slideshows
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0083, WI-0094]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Tests, docs]
---

# WI-0145: Add explicit photo-list collections for manually assembled slideshows

## Objective

Introduce a collection type that stores an explicit ordered set of immutable revision IDs without weakening Smart Collection query semantics.

## Why

A manually assembled slideshow is different from a Smart Collection, which intentionally stores a reusable filter rather than copied revision IDs.

## In scope

- Define a separate explicit/manual collection model with PostgreSQL persistence.
- Store stable revision IDs and deterministic order.
- Expose a snapshot compatible with existing playback.
- Handle missing/excluded revisions safely.
- Keep provenance distinct from query results.

## Out of scope

- Turning Smart Collections into mutable ID lists.
- Collaborative editing.
- Copying photo files into album folders.

## Acceptance criteria

- [ ] An explicit collection persists a named ordered revision list.
- [ ] Its snapshot uses the existing playback resource boundary.
- [ ] Smart Collections remain unchanged.
- [ ] Missing/excluded items follow documented safety rules.
- [ ] Persistence/API tests cover lifecycle and membership.

## Verification requirements

Core/PostgreSQL/API tests before WI-0146 adds UI.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
