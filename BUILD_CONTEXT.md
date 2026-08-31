# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0092 — Add slideshow manual-navigation and orientation preferences** is implemented and in review on PR #223.

The implementation extends the browser-local slideshow settings payload with backward-compatible defaults:

- Manual navigation: On
- Orientation: Current at start

Manual navigation is enforced both by the presentation input guard and SlideshowPlaybackState, so tap/swipe/Left/Right input cannot move photos when disabled while autoplay, Space/parent Play-Pause, parent unlock and recovery remain independent.

Orientation supports Current at start, Portrait and Landscape. Current at start retains exact-orientation-first behavior with family fallback. Portrait/Landscape request the selected Screen Orientation family. Changing the setting during active fullscreen replaces only the application-owned orientation lock; it does not change the snapshot, current photo, wake lock or fullscreen state.

Existing stored M22 slideshow settings without the new fields deserialize to Manual navigation On and Current at start.

Exact-head workflow #1345 passed all lanes: build-and-test, both integration shards, launcher verification and package verification.

WI-0093 and WI-0094 remain proposed and should not be implemented until WI-0092 is merged.

WI-0076 remains separately recorded as in_progress and is not part of this M22 slice.

## Next concrete step

1. Wait for lifecycle-only CI on the current PR #223 head to complete.
2. If green and no review blockers exist, merge PR #223.
3. Then start WI-0093 preparation progress/no-progress recovery.

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
