# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

M22 remains in progress after the 2026-08-30 real-phone maintainer review.

The secure trusted-LAN path, stable slideshow snapshot, fullscreen/protected playback, wake/orientation capability handling and normal slideshow behaviors otherwise worked as expected. The review identified four gaps that must close before M22 completion:

- a setting to disable manual next/previous navigation;
- an explicit slideshow Orientation setting rather than only inheriting orientation at Start;
- Prepare originals needs useful downloading/queued/waiting progress plus no-progress recovery after a 56-photo phone test remained at 1/56 without explanation;
- a read-only basic-user slideshow library is needed to list saved Smart Collections, edit global slideshow settings, start slideshows and prepare originals without entering playback.

These are split into WI-0092, WI-0093 and WI-0094. The consumer page is explicitly a read-only UI boundary, not an authentication/authorization boundary, because Photo Identity remains unauthenticated on the trusted LAN.

The current hydration implementation uses Windows Files On-Demand pinning and bounded concurrency. A ready-only counter can stay unchanged while downloads are active, so WI-0093 first adds aggregate state observability and a safe retry/cancel path rather than speculatively replacing the storage model.

Main workflow #1342 is green after PR #221 corrected packaged startup resilience.

WI-0076 remains separately recorded as in_progress and is not part of this M22 follow-up.

## Next concrete step

1. Maintainer reviews the M22 follow-up contract/docs in the documentation PR.
2. After approval, promote/start WI-0092 and implement manual-navigation/orientation settings.
3. Implement WI-0093 preparation progress/no-progress recovery and verify mixed local/online behavior.
4. Implement WI-0094 read-only slideshow library plus standalone Prepare originals.
5. Repeat focused real-phone M22 acceptance, then complete the M22 work items/milestone.

## Relevant files

- docs/product/slideshow.md
- docs/delivery/milestones/M22-protected-smart-collection-slideshow.md
- docs/delivery/work-items/WI-0092-slideshow-input-orientation-settings.md
- docs/delivery/work-items/WI-0093-slideshow-original-preparation-progress.md
- docs/delivery/work-items/WI-0094-read-only-slideshow-library.md
- src/PhotoIdentity.Web/SlideshowSettings.cs
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor.cs
- src/PhotoIdentity.Web/wwwroot/js/slideshow.js
- src/PhotoIdentity.Api/SlideshowOriginalPreparationService.cs
- src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor
- docs/delivery/status/work-items.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
