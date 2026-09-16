# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience is continuing with WI-0131: restrained adaptive backdrop for contained slideshow photos.**

WI-0129, WI-0130 and WI-0135 have completed maintainer desktop/phone verification and are archived as completed. WI-0130 uses the tuned 3% linear face-aware motion from PR #350.

WI-0131 keeps the foreground photo fully contained and uncropped, but adds a heavily blurred/dimmed same-resource `object-fit: cover` backdrop inside each bounded A/B presentation slot. The whole slot now owns the WI-0129 opacity transition while foreground decode/readiness diagnostics remain attached to the contained image, so backdrop and foreground transition together rather than flashing independently. The backdrop is decorative-only and falls back to black when disabled or unsuitable.

## Next concrete step

Validate the WI-0131 implementation through the normal build/JavaScript/integration/docs gates. Then review representative desktop and phone slideshows across portrait-on-landscape, landscape-on-portrait, bright/dark images and low-resolution proxies. Decide whether the treatment should ship as the default, be simplified, or be rejected; tune fixed backdrop constants rather than adding a viewer-facing style control.

## Relevant files

- docs/delivery/work-items/WI-0131-adaptive-slideshow-backdrop.md
- docs/delivery/status/work-items/active/WI-0131.yaml
- src/PhotoIdentity.Web/Components/SlideshowPresentation.razor
- src/PhotoIdentity.Web/Components/SlideshowPresentation.razor.css
- src/PhotoIdentity.Web/wwwroot/js/slideshow-presentation.js
- tests/javascript/slideshow-prefetch.test.js

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
