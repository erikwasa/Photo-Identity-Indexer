# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0096 — Wait for slideshow preparation cancellation to quiesce** is the active M22 stabilization item.

While validating WI-0095, workflow #1360 reproduced the same WI-0093 integration failure twice. The no-progress/retry test completed its assertions, called Preparation.End, then Windows refused to delete its temporary catalogue.db because the preparation background RunTask still held the file.

The correction makes preparation end asynchronous. The service removes/releases the session, signals cancellation and awaits the existing background RunTask before completion. The HTTP DELETE endpoint and preparation integration tests await that same lifecycle operation. No test cleanup retries or arbitrary delays are added.

WI-0095 remains separately open on PR #226. Its key corrective evidence is already positive: package verification passed on workflow #1360, fixing the post-merge main #1356 ResponseEnded model-download failure. PR #226 remains red only because the WI-0093 cleanup race reproduced.

WI-0082 through WI-0086 and WI-0092 through WI-0094 remain in review pending consolidated real-phone M22 acceptance after the corrective CI gates are green.

WI-0076 remains separately recorded as in_progress and is not part of this M22 slice.

## Next concrete step

1. Run exact-head CI for WI-0096.
2. If green, record evidence and move WI-0096 to in_review.
3. Merge WI-0096 before returning to PR #226.
4. Revalidate PR #226 against the updated main branch; package verification must remain green.
5. Then perform consolidated real-phone M22 acceptance.

## Relevant files

- docs/delivery/work-items/WI-0096-slideshow-preparation-quiescence.md
- src/PhotoIdentity.Api/SlideshowOriginalPreparationService.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationEndpoints.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowOriginalPreparationServiceTests.cs
- docs/delivery/status/work-items.yaml
- docs/delivery/status/milestones.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
