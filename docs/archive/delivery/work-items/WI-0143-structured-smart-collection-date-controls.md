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

- [x] Common year/range searches require no free-text syntax.
- [x] Existing saved filters reopen equivalently.
- [x] Imprecise manual dates follow one documented range-match rule.
- [x] Invalid partial UI states cannot be saved.
- [x] The oversized free-text field is removed.

## Verification requirements

Core/filter compatibility, API persistence and desktop/phone UI verification.

## Completion notes

- Files changed: structured Smart Collection date request contracts/API parsing, responsive Any/Year/Month/Exact/Range controls, editor-state and transient-navigation compatibility helpers, model/API/PostgreSQL integration coverage and delivery tracking.
- Trade-offs: the UI now sends explicit inclusive `from`/`to` bounds and never constructs the old mini-language. The legacy `Taken` request remains accepted for older callers. Existing saved filters already use canonical bounds under filter schema v2, so no persistence migration/version bump is needed; reopening infers the closest equivalent structured control while preserving semantics.
- Deferred work: natural-language dates remain out of scope.
- Verification: maintainer accepted WI-0143 on 2026-09-22 during the combined M28 desktop/phone review.
- Commands run: implementation prepared through the GitHub connector; PR #396 runs the normal build/integration/docs/package/PostgreSQL CI gates.
