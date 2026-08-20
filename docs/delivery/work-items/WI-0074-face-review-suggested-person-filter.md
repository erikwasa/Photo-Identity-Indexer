---
id: WI-0074
title: Filter Face Review by suggested person
milestone: M20
status_source: ../status/work-items.yaml
depends_on: [WI-0043, WI-0047]
related_adrs: []
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0074: Filter Face Review by suggested person

## Objective

Allow the maintainer to restrict Face Review to faces whose current rank-one identity suggestion is a selected canonical person, while preserving all existing review-state, model, confidence and navigation semantics.

## Contract

- Add optional `suggestedPersonId` queue/filter semantics meaning: **the current rank-one/top suggestion is this canonical person**.
- Do not silently broaden the criterion to lower-ranked suggestions.
- Compose independently with review state, processing run, exact suggestion model revision, confidence group and existing ordering modes.
- Add a searchable single-person selector in Face Review controls.
- Smart Collection visibility preferences must not hide people from identity review; hidden-from-Smart-Collections people remain selectable here.
- Preserve `suggestedPersonId` through Face Details previous/next queue navigation and return URLs.
- Empty-result behavior remains explicit and stable.
- If a future requirement needs `any ranked suggestion`, model it as a separate explicit mode.

## Acceptance criteria

- [x] Person-only filtering returns only faces whose current rank-one suggestion matches the selected canonical person.
- [x] Person + confidence group composes correctly.
- [x] Person + ordering/model/review-state criteria compose correctly.
- [x] Hidden-from-Smart-Collections people remain discoverable/selectable in Face Review.
- [x] Face Details previous/next navigation stays inside the filtered queue.
- [x] Returning from Face Details restores the selected suggested-person filter.
- [x] Removing/clearing the filter restores normal queue semantics.
- [x] Repository/API/component/integration coverage includes empty results and stale/non-current suggestion cases.

## Implementation evidence

- PR #197 implements one parameterized `suggestedPersonId` predicate on the existing exact-model pending rank-one suggestion CTE; there is no schema or migration change and lower-ranked suggestions never participate in the predicate.
- Face Review uses a searchable single-person picker over the complete `/api/review/people` response. People hidden from Smart Collections remain candidates rather than being filtered out.
- The selected person is carried through gallery paging, generated Face Details URLs, previous/next suggestion-queue navigation and the Face Review return URL. Clearing suggestion-model context clears the person filter.
- Exact-head workflow #1218 (`32312949819`) passed the final merged implementation validation.

## Maintainer verification — 2026-08-21

The consolidated M20 browser review confirmed that suggested-person filtering works as intended, composes with the existing queue controls, and preserves Face Details navigation/return context. No corrective filtering-semantic work is requested.

Compact picker presentation changes requested during the same review are tracked under WI-0073 because they are UI density/presentation corrections rather than changes to WI-0074 filtering semantics.

See `../milestones/M20-maintainer-review-2026-08-21.md`.

## Source finding

The 2026-08-19 maintainer review explicitly requested this filter after the existing suggestion-gallery/model/confidence workflow was verified. The semantic anchor is the current rank-one suggestion, not display name and not any historical/lower-ranked suggestion.
