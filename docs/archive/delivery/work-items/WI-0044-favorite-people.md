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

Automated persistence/query tests plus browser verification on desktop and narrow/mobile layouts. Human desktop and Pixel verification completed on 2026-08-11 as part of the milestone-wide M17 review.

## Completion notes

- Files changed: favorite-person persistence, review/maintenance API contracts and endpoints, people maintenance controls, Face Details favorite control, face-card assignment labels, collection ordering, and integration coverage.
- Integration resolved: after WI-0043 merged, WI-0047 schema version 12 promoted `person_favorites` into the central SQLite migration lifecycle and moved favorite OR-consolidation into the canonical person-merge transaction. The temporary branch-isolation trade-off is therefore closed before M17 review.
- Merge policy: the surviving person is favorite when either the source or target person was favorite; rename never changes favorite state.
- Matcher behavior: favorites remain a UI preference only and do not alter matcher evidence or scores.
- Verification: PR #117 merged to `m17`; post-conflict GitHub Actions run `31435193480` passed Release build, full tests, documentation checks, review-app smoke, and Windows mixed-media verification. The maintainer then accepted favorite persistence, ordering and controls during the milestone-wide Windows laptop and Pixel verification on 2026-08-11.
- Deferred work: none required for WI-0044 completion. The remaining Faces layout comments are minor UI follow-ups and do not change favorite behavior.
