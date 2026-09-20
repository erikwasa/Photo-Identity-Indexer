# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience has completed WI-0132, WI-0133 and WI-0139 after combined maintainer verification.**

The maintainer accepted the moment-aware chapter transition treatment, burst/near-duplicate compact pacing, and no-Fullscreen-API fallback on 2026-09-20. Their implementation PRs and CI evidence are recorded in the archived work-item status shards.

WI-0137 is now the only remaining M27 work item and is `ready`. It owns the final integrated real-device acceptance/tuning pass before M27 can be completed.

## Next concrete step

Run WI-0137 across the representative supported iPhone/browser path and at least one desktop browser, covering the combined slideshow experience rather than re-verifying each completed work item in isolation.

## Relevant files

- docs/delivery/work-items/WI-0137-real-device-slideshow-polish-acceptance.md
- docs/delivery/status/work-items/active/WI-0137.yaml
- docs/delivery/status/work-items/archive/WI-0132.yaml
- docs/delivery/status/work-items/archive/WI-0133.yaml
- docs/delivery/status/work-items/archive/WI-0139.yaml
- docs/delivery/milestones/M27-slideshow-presentation-experience.md
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Components/SlideshowPresentation.razor
- src/PhotoIdentity.Web/SlideshowPlaybackState.cs
- src/PhotoIdentity.Web/SlideshowTimingPolicy.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
