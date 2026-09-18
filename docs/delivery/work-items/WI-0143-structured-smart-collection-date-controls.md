---
id: WI-0143
title: Replace Smart Collection date free text with structured controls
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0141]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0143: Replace Smart Collection date free text with structured controls

## Objective

Replace the Smart Collection date mini-language with compact structured date controls.

## Why

The current free-text field exposes parser syntax instead of the simpler From/To model users actually need.

## In scope

- Provide structured any/year/month/date/range controls.
- Persist a versioned filter without user-facing syntax.
- Match effective dates from WI-0141, including imprecise manual ranges.
- Preserve existing saved date-filter compatibility.
- Keep missing-date behavior explicit.

## Out of scope

- Natural-language parsing.
- Arbitrary temporal query languages.

## Acceptance criteria

- [ ] Common year/range searches require no free-text syntax.
- [ ] Existing saved filters reopen equivalently.
- [ ] Imprecise manual dates follow one documented range-match rule.
- [ ] Invalid partial UI states cannot be saved.
- [ ] The oversized free-text field is removed.

## Verification requirements

Core/filter compatibility, API persistence and desktop/phone UI verification.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
