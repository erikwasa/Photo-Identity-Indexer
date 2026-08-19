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

- [ ] Person-only filtering returns only faces whose current rank-one suggestion matches the selected canonical person.
- [ ] Person + confidence group composes correctly.
- [ ] Person + ordering/model/review-state criteria compose correctly.
- [ ] Hidden-from-Smart-Collections people remain discoverable/selectable in Face Review.
- [ ] Face Details previous/next navigation stays inside the filtered queue.
- [ ] Returning from Face Details restores the selected suggested-person filter.
- [ ] Removing/clearing the filter restores normal queue semantics.
- [ ] Repository/API/component/integration coverage includes empty results and stale/non-current suggestion cases.

## Implementation evidence

- PR #197 implements one parameterized `suggestedPersonId` predicate on the existing exact-model pending rank-one suggestion CTE; there is no schema or migration change and lower-ranked suggestions never participate in the predicate.
- Face Review uses a searchable single-person picker over the complete `/api/review/people` response. People hidden from Smart Collections remain candidates and are labelled as such rather than filtered out.
- The selected person is carried through gallery paging, generated Face Details URLs, previous/next suggestion-queue navigation and the Face Review return URL. Clearing suggestion-model context clears the person filter.
- Workflow #1213 (`32311872263`) built the implementation successfully. Its focused shared-host test `SuggestedPersonReviewFilterApplicationTests.Suggested_person_filter_uses_only_current_rank_one_and_composes_with_review_scope` passed in integration shard 1 in 2.41s and covers rank-one-only behavior, lower-ranked and stale/unranked exclusion, confidence/review-state composition, hidden-person availability, empty results, invalid person ids and filtered previous/next navigation.
- Acceptance checkboxes remain open until the focused browser/maintainer pass verifies the interactive picker and return-context behavior end to end.

## Source finding

The 2026-08-19 maintainer review explicitly requested this filter after the existing suggestion-gallery/model/confidence workflow was verified. The semantic anchor is the current rank-one suggestion, not display name and not any historical/lower-ranked suggestion.
