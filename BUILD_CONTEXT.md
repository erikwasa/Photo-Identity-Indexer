# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience is batching its remaining presentation integrations before combined maintainer review.**

WI-0132 now carries Creative Collection moment annotations into the immutable slideshow snapshot and uses a restrained chapter-boundary crossfade without changing collection membership/order. It remains `in_progress` until the planned combined M27 device review.

WI-0139 is also implemented and merged but remains `in_progress` pending the same real-device review of the iPhone no-Fullscreen-API fallback and desktop fullscreen recovery.

## Next concrete step

Implement WI-0133 burst-aware compact pacing, then run the combined M27 review covering WI-0132, WI-0133 and WI-0139 before starting/closing WI-0137.

## Relevant files

- docs/delivery/work-items/WI-0132-moment-aware-slideshow-transitions.md
- docs/delivery/work-items/WI-0133-burst-aware-slideshow-pacing.md
- docs/delivery/work-items/WI-0139-no-fullscreen-slideshow-fallback.md
- docs/delivery/work-items/WI-0137-real-device-slideshow-polish-acceptance.md
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Components/SlideshowPresentation.razor
- src/PhotoIdentity.Web/SlideshowTimingPolicy.cs
- src/PhotoIdentity.Web/wwwroot/js/slideshow-presentation.js

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
