# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience is continuing with WI-0135: redesign the slideshow library as a visual tap-to-play gallery.**

WI-0129 is merged but intentionally remains `in_progress` until the deferred real-device slideshow acceptance is performed together with later M27 visual changes. Do not mark it complete merely to unblock dependent presentation work.

WI-0135 keeps the fast `/api/slideshows/collections` definition list unchanged. Each visual card lazily queries the existing saved-collection endpoint for its deterministic first matching photo and uses that thumbnail as a decorative cover; empty/error cases retain a fixed neutral fallback. The whole card is the accessible Play action, while preparation controls and browser-local playback preferences are visually secondary.

## Next concrete step

Validate the WI-0135 gallery implementation: compile the new cover component and presentation helper, run slideshow/integration tests, verify documentation generation, and review the `/slideshows` layout at desktop and iPhone-sized widths. Preserve existing fullscreen-rejection recovery through `SlideshowLibraryLaunch`.

## Relevant files

- docs/delivery/work-items/WI-0135-visual-tap-to-play-slideshow-library.md
- docs/delivery/status/work-items/active/WI-0135.yaml
- src/PhotoIdentity.Web/Pages/Slideshows.razor
- src/PhotoIdentity.Web/Pages/Slideshows.razor.css
- src/PhotoIdentity.Web/Pages/Slideshows.razor.cs
- src/PhotoIdentity.Web/Components/SlideshowLibraryCover.razor
- src/PhotoIdentity.Web/Components/SlideshowLibraryCover.razor.css
- src/PhotoIdentity.Web/SlideshowLibraryPresentation.cs
- src/PhotoIdentity.Web/SlideshowLibraryLaunch.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowLibraryAcceptanceTests.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowLibraryPresentationTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
