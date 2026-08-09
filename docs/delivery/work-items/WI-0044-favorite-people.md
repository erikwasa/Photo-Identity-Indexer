---
id: WI-0044
title: Add favorite people
milestone: M17
status_source: ../status/work-items.yaml
depends_on: [WI-0015]
related_adrs: []
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0044: Add favorite people

## Objective

Allow the maintainer to mark frequently used people as favorites so they appear before non-favorites anywhere a person is selected or browsed.

## Why

The canonical people list will become increasingly long. Alphabetical selection alone makes repeated assignment to close family and other common identities unnecessarily slow.

## In scope

- Persist a favorite preference independently from model scoring.
- Add favorite/unfavorite controls to person maintenance and useful assignment surfaces.
- Order favorites first and alphabetically within the favorite and non-favorite groups.
- Apply the same ordering to individual assignment, bulk assignment, people maintenance and collection person selectors.
- Preserve favorite state across rename and define deterministic behavior when people are merged.

## Out of scope

- Using favorites as matching priors or changing identity scores.
- Multiple favorite tiers.

## Acceptance criteria

- [ ] A person can be marked and unmarked as favorite.
- [ ] Favorite state persists across application restarts and person rename.
- [ ] Favorites appear first in all normal person selectors with stable alphabetical ordering inside each group.
- [ ] Favorite state does not affect matcher evidence or scores.
- [ ] Person merge has defined, tested favorite-state behavior.

## Verification requirements

Automated persistence/query tests plus browser verification on desktop and narrow/mobile layouts.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
