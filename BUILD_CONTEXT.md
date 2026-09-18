# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience is ready for its final real-device acceptance item, WI-0137.**

WI-0131 adaptive backdrop, WI-0134 adaptive rhythm and WI-0136 exception-driven preparation were accepted by the maintainer on 2026-09-18 and are now completed. Together with the previously completed M27 core items, that unblocks WI-0137 as the milestone-level device/polish acceptance pass.

WI-0137 should evaluate the combined slideshow experience rather than reopening the individual accepted features unless the integrated pass exposes a reproducible regression.

## Next concrete step

Start WI-0137 and run the representative combined slideshow acceptance set on the supported iPhone/browser path and at least one desktop browser. Cover autoplay, transitions/readiness, rapid manual navigation, pause/resume, looping, reduced motion, backdrop/motion/timing defaults, tap-to-play library flow, exception-driven preparation, protected controls, fullscreen/orientation recovery and browser performance/resource behavior.

## Relevant files

- docs/delivery/work-items/WI-0137-real-device-slideshow-polish-acceptance.md
- docs/delivery/status/work-items/active/WI-0137.yaml
- docs/delivery/work-items/WI-0131-adaptive-slideshow-backdrop.md
- docs/delivery/work-items/WI-0134-adaptive-slideshow-rhythm.md
- docs/delivery/work-items/WI-0136-exception-driven-slideshow-preparation.md
- src/PhotoIdentity.Web/Pages/Slideshows.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Components/SlideshowPresentation.razor

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
