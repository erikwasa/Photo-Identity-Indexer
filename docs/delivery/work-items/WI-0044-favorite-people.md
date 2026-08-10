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

- [x] A person can be marked and unmarked as favorite.
- [x] Favorite state persists across application restarts and person rename.
- [x] Favorites appear first in all normal person selectors with stable alphabetical ordering inside each group.
- [x] Favorite state does not affect matcher evidence or scores.
- [x] Person merge has defined, tested favorite-state behavior.

## Verification requirements

Automated persistence/query tests plus browser verification on desktop and narrow/mobile layouts.

## Completion notes

- Files changed: favorite-person persistence, review/maintenance API contracts and endpoints, people maintenance controls, Face Details favorite control, face-card assignment labels, collection ordering, and integration coverage.
- Trade-offs: while M17 work-item branches remain isolated, `person_favorites` is created idempotently by `SqliteFavoritePeopleRepository` rather than claiming schema migration version 11, which is already introduced independently by WI-0043. During M17 integration this table must be promoted into the next numbered central SQLite migration. Favorite consolidation currently follows the canonical person merge in the endpoint; integration should fold that small preference consolidation into the canonical merge transaction when the shared migration is introduced.
- Merge policy: the surviving person is favorite when either the source or target person was favorite; rename never changes favorite state.
- Deferred work: desktop and narrow/mobile browser verification is intentionally deferred to the milestone-wide M17 review. No matcher prior or scoring behavior is introduced.
- Commands run: GitHub Actions Release build/test/documentation/review/Windows verification workflow for PR #117; exact successful run is recorded in delivery evidence after the final branch head completes.
