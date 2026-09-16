# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience is continuing with WI-0134: bounded adaptive timing for a less mechanical slideshow rhythm.**

WI-0129, WI-0130 and WI-0135 have completed maintainer desktop/phone verification and are archived as completed. WI-0131 is merged in PR #352 but intentionally remains `in_progress` until the deferred visual/device review is performed.

WI-0134 treats the persisted image duration as the dominant pace and reuses the face-geometry evidence already loaded by `SlideshowPresentation`. Timing is deterministic and narrowly bounded: reliable single-face frames use 98% of the configured duration, two-face frames 104%, groups 108%, while no-face or unavailable/unreliable evidence uses the configured duration exactly. The effective duration lives in `SlideshowPlaybackState` for progress/timer diagnostics and is not exposed as viewer chrome.

## Next concrete step

Validate the WI-0134 implementation through the normal build/integration/docs gates. Pay particular attention to the generic presentation callback, manual-navigation ready-before-timer behavior, pause/resume, progress fraction and duration-setting changes. Subjective rhythm comparison can be batched with the later M27 visual review rather than blocking this implementation PR.

## Relevant files

- docs/delivery/work-items/WI-0134-adaptive-slideshow-rhythm.md
- docs/delivery/status/work-items/active/WI-0134.yaml
- src/PhotoIdentity.Web/SlideshowTimingPolicy.cs
- src/PhotoIdentity.Web/SlideshowPlaybackState.cs
- src/PhotoIdentity.Web/Components/SlideshowPresentation.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- tests/PhotoIdentity.Integration.Tests/SlideshowTimingPolicyTests.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowPlaybackStateTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
