# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience has its remaining implementation work ready for combined maintainer verification.**

WI-0132 carries Creative Collection moment annotations into the immutable slideshow snapshot and uses a restrained chapter-boundary crossfade without changing collection membership/order.

WI-0133 now carries accepted WI-0123 visual-redundancy groups into Creative slideshow snapshots and applies bounded compact timing only to autoplay continuations inside the same group. Manual destinations and loop restarts retain normal/adaptive timing.

WI-0139 provides the no-Fullscreen-API fallback while keeping reduced browser-level protection explicit.

All three remain `in_progress` until the planned combined M27 real-device review.

## Next concrete step

After the WI-0133 implementation PR is merged, run the combined M27 review for WI-0132, WI-0133 and WI-0139 on the supported iPhone/browser path and a desktop browser. Then start/complete WI-0137 as the final integrated acceptance gate.

## Relevant files

- docs/delivery/work-items/WI-0132-moment-aware-slideshow-transitions.md
- docs/delivery/work-items/WI-0133-burst-aware-slideshow-pacing.md
- docs/delivery/work-items/WI-0139-no-fullscreen-slideshow-fallback.md
- docs/delivery/work-items/WI-0137-real-device-slideshow-polish-acceptance.md
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/SlideshowPlaybackState.cs
- src/PhotoIdentity.Web/SlideshowTimingPolicy.cs
- src/PhotoIdentity.Api/CreativeCollectionMaterializationService.cs
- src/PhotoIdentity.Api/CreativeCollectionPreviewEndpoints.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
