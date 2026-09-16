# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience is starting with WI-0129: a predecoded dual-layer slideshow transition renderer.**

The existing slideshow already keeps autoplay timing separate from image readiness and has a bounded adjacent-image prefetch pipeline. WI-0129 replaces the single keyed `<img>` presentation with a bounded current/incoming compositor so the next image can load and decode while the current photo remains visible, then crossfade without a black flash.

The renderer must preserve M22 protected/fullscreen/original-preparation behavior and M24 slideshow performance diagnostics. Rapid manual navigation should serialize/coalesce through the existing navigation gate. Reduced-motion users still get ready-before-show swaps without nonessential animation.

## Next concrete step

Implement the presentation host/compositor in `slideshow.js`, wire `Slideshow.razor`/`Slideshow.razor.cs` so playback marks the destination ready only after the compositor reports it visible, and extend JavaScript plus playback-state regression tests for initial presentation, crossfade, reduced motion, rapid navigation/loop behavior and failures.

## Relevant files

- docs/delivery/work-items/WI-0129-dual-layer-slideshow-renderer.md
- docs/delivery/status/work-items/active/WI-0129.yaml
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor.cs
- src/PhotoIdentity.Web/Pages/Slideshow.razor.css
- src/PhotoIdentity.Web/SlideshowPlaybackState.cs
- src/PhotoIdentity.Web/SlideshowNavigationGate.cs
- src/PhotoIdentity.Web/wwwroot/js/slideshow.js
- src/PhotoIdentity.Web/wwwroot/js/slideshow-performance.js
- tests/javascript/slideshow-prefetch.test.js
- tests/PhotoIdentity.Integration.Tests/SlideshowPlaybackStateTests.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowJavascriptTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
