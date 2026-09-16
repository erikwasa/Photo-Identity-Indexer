---
id: WI-0122
title: Add photo presentation preferences for Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0120]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0122: Add photo presentation preferences for Creative Collections

## Objective

Allow explicit photo-level presentation preferences to influence Creative Collection selection without changing archive, identity or Smart Collection truth.

## Why

Automatic selectors cannot know every personally important or undesirable photo. Small explicit signals such as prefer, avoid and always-include-when-relevant can provide high-value correction while also producing useful evidence for later selector tuning.

## In scope

- Define presentation-only photo preferences separately from tags, people, Places and source exclusion.
- Support at least Prefer and Avoid; evaluate whether a stronger Pin/Always include when relevant action is useful without making target counts impossible.
- Preserve append-only/auditable preference changes using existing photo-action design patterns.
- Incorporate active preferences into Creative Collection selection with deterministic precedence.
- Expose preference editing from appropriate photo/library surfaces without changing ordinary Smart Collection membership.

## Out of scope

- Deleting or privacy-excluding a source photo.
- Automatically learning preferences from skips in this work item.
- A general-purpose rating/star system unrelated to presentation.

## Acceptance criteria

- [ ] Presentation preferences are persisted independently from canonical photo/identity metadata.
- [ ] Avoid prevents normal Creative Collection selection while keeping the photo browsable elsewhere.
- [ ] Prefer measurably increases selection priority when the photo is otherwise eligible.
- [ ] Preference changes are reversible/auditable and deterministic selection tests cover precedence and target-count edge cases.
- [ ] Existing exact Smart Collection results remain unchanged.

## Verification requirements

Automated persistence/selection tests plus maintainer verification that preferences affect a representative Creative Collection without hiding photos from ordinary library use.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
