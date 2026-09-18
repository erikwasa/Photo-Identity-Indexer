---
id: WI-0156
title: Evaluate iPhone Live Photo pairing as one logical media experience
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, docs]
---

# WI-0156: Evaluate iPhone Live Photo pairing as one logical media experience

## Objective

Determine whether paired still + MOV Live Photo assets should remain independent or be presented as one logical media item.

## Why

Naively treating both companions as unrelated can duplicate library/slideshow results, while premature pairing can damage source identity semantics.

## In scope

- Identify reliable pairing metadata in the maintained archive.
- Preserve both immutable source/revision identities.
- Evaluate browsing, Smart Collection and slideshow implications.
- Define fallback when one companion is missing/unsupported.

## Out of scope

- Deleting/merging physical source assets.
- Filename-only pairing assumptions.
- Blocking ordinary video support on pairing.

## Acceptance criteria

- [ ] Representative evidence establishes pairing reliability.
- [ ] Any logical grouping preserves both revisions/provenance.
- [ ] An adopt/defer/no-go decision is documented independently of basic video support.

## Verification requirements

Representative private archive inspection and documented decision.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
