# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0093 — Make slideshow original preparation observable and recoverable** is implemented and in review on PR #224.

The implementation extends the existing full-snapshot preparation session rather than changing hydration ownership or storage admission semantics. Preparation status now carries path-free aggregate counts for ready, downloading, queued and waiting-for-release work, plus hydration request count, phase, last-progress time and elapsed no-progress duration.

A centralized two-minute no-progress threshold produces a warning rather than an automatic failure. The preparation session continues to reconcile safely while playback remains paused. Retry keeps the same server session and immutable revision set, resets the no-progress observation window and wakes the existing reconciliation loop immediately; it does not create a new Smart Collection snapshot or parallel hydration worker.

The slideshow UI renders activity counts and opens protected parent controls when the no-progress warning appears. Parent controls then offer Retry preparation and Cancel preparation. Capacity failure remains a separate failed state with the existing available/proxy fallback.

Focused tests cover the real-phone shape of one ready + one downloading + one queued item under concurrency 1, and a stalled downloading state whose warning can be retried without changing the session ID or hydration ownership.

Exact-head workflow #1349 passed all lanes: build-and-test, both integration shards, launcher verification and package verification.

WI-0094 remains proposed and should reuse this finalized preparation status/retry contract.

WI-0076 remains separately recorded as in_progress and is not part of this M22 slice.

## Next concrete step

1. Wait for lifecycle-only CI on the current PR #224 head to complete.
2. If green and no review blockers exist, merge PR #224.
3. Then start WI-0094 read-only slideshow library with standalone Prepare originals.

## Relevant files

- docs/delivery/work-items/WI-0093-slideshow-original-preparation-progress.md
- src/PhotoIdentity.Api/SlideshowOriginalPreparationService.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationEndpoints.cs
- src/PhotoIdentity.Web/SlideshowOriginalPreparationContracts.cs
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor.cs
- src/PhotoIdentity.Web/SlideshowProtection.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowOriginalPreparationServiceTests.cs
- docs/delivery/status/work-items.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
