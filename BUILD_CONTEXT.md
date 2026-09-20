# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience is completed.**

WI-0137 passed the final real-device acceptance on 2026-09-20 using an iPhone 16e on iOS 27 and Windows with Microsoft Edge. The combined slideshow flow, protected/fullscreen recovery, no-fullscreen fallback, reduced motion, slower readiness and performance diagnostics were accepted without further M27 runtime tuning.

Phone diagnostics over 25 displayed presentations showed 23 prefetch hits / 2 misses, 68.6 ms average Resource Timing, 26 viewer-preview opens and zero hash reads, with no systematic latency growth. The current M27 transition, motion, backdrop and adaptive pacing constants remain the accepted defaults.

M26 remains active with WI-0128 caption quality accepted and the product integration being corrected to archive-level photo enrichment. Caption generation is global/background/default-off; slideshows and other consumers only read persisted evidence. M28 and M29 are ready. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Continue the selected active/ready roadmap work. Do not reopen M27 presentation tuning unless new evidence identifies a reproducible regression.

## Relevant files

- docs/delivery/milestones/M27-slideshow-presentation-experience.md
- docs/delivery/work-items/WI-0137-real-device-slideshow-polish-acceptance.md
- docs/delivery/status/work-items/archive/WI-0137.yaml
- docs/operations/slideshow-browser-performance-diagnostics.md
- docs/delivery/milestones/M26-creative-collections.md
- docs/delivery/status/work-items/active/WI-0128.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
