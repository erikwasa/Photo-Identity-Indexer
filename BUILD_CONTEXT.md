# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**M20 — Operator polish and throughput** is the active delivery focus.

**WI-0073 — Polish cards, menus and archive navigation** is implemented and merged through PR #196 (`bb52ae10470262e6e7a0573a0b70671af207ca49`). Exact-head workflow #1211 (`32309615851`) passed build/fast tests, both required integration shards, documentation checks, published review, mixed-media, launcher and package verification. WI-0073 remains `in_review` until its focused browser/maintainer checks are completed.

**WI-0074 — Filter Face Review by suggested person** is implemented in PR #197 on `agent/WI-0074-suggested-person-filter`. The filter is anchored to the current pending rank-one suggestion for one exact model revision; it composes with review state, processing run, confidence group and ordering, and is preserved through Face Details navigation/return context. The Face Review selector uses the complete review-person list, including people hidden from Smart Collections.

PR #197 workflow #1213 (`32311872263`) proved the solution build, fast tests, documentation checks, published review, mixed-media, launcher and package paths. The focused shared-host test `SuggestedPersonReviewFilterApplicationTests.Suggested_person_filter_uses_only_current_rank_one_and_composes_with_review_scope` passed in integration shard 1 in 2.41s and covers rank-one-only semantics, lower-ranked/stale exclusion, confidence/state composition, hidden-person availability, empty results, invalid ids and filtered previous/next navigation.

## Next concrete step

1. Complete the final exact-head CI run after lifecycle/documentation reconciliation.
2. If green, mark PR #197 ready for review without merging it.
3. Browser-verify WI-0074: searchable person selection (including hidden people), model/confidence/state composition, empty results, Face Details previous/next staying inside the selected-person queue, return restoring the selected person, and clearing restoring the normal queue.
4. Keep WI-0073 and WI-0074 `in_review` until those maintainer/browser checks pass.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0073-ui-navigation-polish.md`
- `docs/delivery/work-items/WI-0074-face-review-suggested-person-filter.md`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSuggestionGalleryRepository.cs`
- `src/PhotoIdentity.Api/SuggestionGalleryEndpoints.cs`
- `src/PhotoIdentity.Web/Components/ReviewWorkspace.razor`
- `src/PhotoIdentity.Web/Components/ReviewSuggestedPersonPicker.razor`
- `src/PhotoIdentity.Web/Pages/FaceDetails.razor`
- `tests/PhotoIdentity.Integration.Tests/SuggestedPersonReviewFilterApplicationTests.cs`
- `AGENTS.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
