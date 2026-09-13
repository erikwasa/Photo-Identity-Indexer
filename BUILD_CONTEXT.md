# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery is accepted and complete. WI-0111 bounded follow-up regeneration is implemented and merged in PR #322 with green CI, but remains `in_review` because the maintainer deferred the required Windows acceptance pass until the next work-item verification.

The production execution strategy remains local under ADR-0010, PostgreSQL remains the sole writable production catalogue, M23 remains intentionally deferred, and WI-0081 remains the separate quality investigation that gates any later expansion of automatic identity assignment.

## Next concrete step

Implement WI-0112: add a suggested-person grouped review workspace over existing pending exact-model rank-1 suggestions. Keep queries bounded, show group count/confidence summaries and representative faces, and reuse existing selection plus audited bulk suggestion acceptance/rejection semantics. Do not introduce clustering or change thresholds/scoring.

When WI-0112 is ready for maintainer verification, include the deferred WI-0111 Windows acceptance in the same verification session before marking WI-0111 completed.

## Relevant files

- docs/delivery/milestones/M25-face-discovery-and-cluster-assisted-review.md
- docs/delivery/work-items/WI-0112-suggested-person-review-workspace.md
- docs/delivery/status/work-items/active/WI-0112.yaml
- docs/delivery/work-items/WI-0111-event-driven-match-regeneration.md
- docs/decisions/ADR-0006-canonical-auto-assignment.md
- src/PhotoIdentity.Core/Review/ISuggestionGalleryRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresSuggestionGalleryRepository.cs
- src/PhotoIdentity.Api/SuggestionGalleryEndpoints.cs
- src/PhotoIdentity.Api/BulkSuggestionReviewEndpoints.cs
- src/PhotoIdentity.Web/Pages/Home.razor

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
