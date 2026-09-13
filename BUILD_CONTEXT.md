# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery is accepted and complete. WI-0111 bounded follow-up regeneration is implemented and merged in PR #322 with green CI, but remains `in_review` because the maintainer deferred the required Windows acceptance pass until the WI-0112 verification session.

WI-0112 suggested-person grouped review is implemented in PR #324. The new surface groups current unreviewed/pending exact-model rank-1 suggestions by person, exposes bounded confidence/count/representative summaries, and then reuses the existing filtered Faces workspace for member paging, subset selection, exceptions and audited suggestion decisions. It does not add clustering, automatic group acceptance, schema changes or threshold/scoring changes.

The production execution strategy remains local under ADR-0010, PostgreSQL remains the sole writable production catalogue, M23 remains intentionally deferred, and WI-0081 remains the separate quality investigation that gates any later expansion of automatic identity assignment.

## Next concrete step

Finish CI for PR #324, merge the WI-0111 deferred-verification bookkeeping PR #323 first, retarget #324 to `main`, and merge #324 once its final CI is green.

After #324 merges, run one combined maintainer verification session:

- WI-0111: make a manual identity assignment without pressing Regenerate and observe automatic `queued` → `running` → `current`, then verify disabled mode still permits explicit regeneration.
- WI-0112: open `Suggested groups`, choose the production exact model, review a person with multiple pending suggestions, accept only a subset, leave/remove an exception, reject one incorrect face-person suggestion from Details, confirm group counts update, and repeat the key subset flow on mobile/touch.

Do not mark WI-0111 or WI-0112 completed until that human evidence is recorded.

## Relevant files

- docs/delivery/work-items/WI-0111-event-driven-match-regeneration.md
- docs/delivery/work-items/WI-0112-suggested-person-review-workspace.md
- docs/delivery/status/work-items/active/WI-0111.yaml
- docs/delivery/status/work-items/active/WI-0112.yaml
- src/PhotoIdentity.Core/Review/ISuggestedPersonGroupRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresSuggestedPersonGroupRepository.cs
- src/PhotoIdentity.Persistence.Sqlite/SqliteSuggestedPersonGroupRepository.cs
- src/PhotoIdentity.Api/SuggestionGalleryEndpoints.cs
- src/PhotoIdentity.Web/Pages/SuggestedPersonGroups.razor
- src/PhotoIdentity.Web/Components/ReviewWorkspace.razor
- tests/PhotoIdentity.Integration.Tests/SuggestedPersonGroupApplicationTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
