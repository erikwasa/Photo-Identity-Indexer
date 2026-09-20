# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**WI-0137 is in progress as the final M27 real-device acceptance/tuning gate.**

WI-0132, WI-0133 and WI-0139 are completed and archived after combined maintainer verification. WI-0137 now has a repeatable acceptance protocol covering the supported iPhone/browser path, desktop true fullscreen/recovery, reduced motion, slower image readiness, protected controls, looping and the existing M24 browser-performance diagnostics.

No new runtime feature is planned for WI-0137 unless the acceptance pass exposes a reproducible defect. The current M27 visual/timing constants are recorded in the work item so they can be accepted or tuned centrally without adding viewer-facing settings.

## Next concrete step

Run the WI-0137 protocol in `docs/delivery/work-items/WI-0137-real-device-slideshow-polish-acceptance.md` on the supported iPhone/browser path and at least one desktop browser. Capture the browser diagnostics report after at least 10 displayed photos and record device/browser versions plus any repeatable visual or performance defects.

If the pass is clean, update WI-0137 and M27 to completed with the maintainer evidence. If a defect is found, fix only that evidence-backed issue and repeat the affected slice.

## Relevant files

- docs/delivery/work-items/WI-0137-real-device-slideshow-polish-acceptance.md
- docs/operations/slideshow-browser-performance-diagnostics.md
- docs/operations/slideshow-performance-diagnostics.md
- docs/delivery/status/work-items/active/WI-0137.yaml
- docs/delivery/milestones/M27-slideshow-presentation-experience.md
- src/PhotoIdentity.Web/Components/SlideshowPresentation.razor
- src/PhotoIdentity.Web/SlideshowMotionPolicy.cs
- src/PhotoIdentity.Web/SlideshowTimingPolicy.cs
- src/PhotoIdentity.Web/wwwroot/js/slideshow-presentation.js

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
