# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0092 — Add slideshow manual-navigation and orientation preferences** is the active M22 phone-review follow-up.

The implementation extends the existing browser-local slideshow settings payload with backward-compatible defaults:

- Manual navigation: On
- Orientation: Current at start

Manual navigation is enforced both by the presentation input guard and SlideshowPlaybackState, so tap/swipe/Left/Right input cannot move photos when disabled while autoplay, Space/parent Play-Pause, parent unlock and recovery remain independent.

Orientation supports Current at start, Portrait and Landscape. Current at start retains the existing exact-orientation-first behavior with family fallback. Portrait/Landscape request the selected Screen Orientation family. Changing the setting during active fullscreen replaces only the application-owned orientation lock; it does not change the snapshot, current photo, wake lock or fullscreen state.

Existing stored M22 slideshow settings without the two new fields deserialize to Manual navigation On and Current at start.

WI-0093 and WI-0094 remain proposed and should not be implemented until WI-0092 is through its implementation review.

WI-0076 remains separately recorded as in_progress and is not part of this M22 slice.

## Next concrete step

1. Run exact-head CI for WI-0092 and correct any build/Razor/test failures.
2. When green, record PR/workflow evidence and move WI-0092 to in_review.
3. Merge WI-0092 after the final lifecycle-only CI is green.
4. Then start WI-0093 preparation progress/no-progress recovery.

## Relevant files

- docs/delivery/work-items/WI-0092-slideshow-input-orientation-settings.md
- src/PhotoIdentity.Web/SlideshowSettings.cs
- src/PhotoIdentity.Web/SlideshowPlaybackState.cs
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor.cs
- src/PhotoIdentity.Web/wwwroot/js/slideshow.js
- tests/PhotoIdentity.Integration.Tests/SlideshowSettingsTests.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowPlaybackStateTests.cs
- docs/delivery/status/work-items.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
