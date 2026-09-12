# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M22 Protected Smart Collection slideshow is completed and accepted.**

PR #314 corrected the final direct-fullscreen and prepared-state persistence gaps. Follow-up PR #315 corrected the browser-format preparation blocker so a local immutable-revision-verified original no longer fails preparation merely because the browser cannot render its media type directly; unsupported prepared media uses the established durable proxy fallback at presentation time.

On 2026-09-13 the maintainer confirmed on the supported phone/browser that **Prepare originals** succeeds, **Start slideshow** enters fullscreen directly without an intermediate application fullscreen step, and **Originals prepared** survives starting the slideshow, deliberately exiting, and returning to `/slideshows` while the exact prepared set remains reusable.

The optional manual stale-receipt downgrade scenario was not performed at maintainer direction. Existing automated revalidation coverage is accepted for that path and verifies downgrade when membership changes or an original becomes non-reusable.

All eleven M22 work items are completed and archived. M22 is completed. M23 remains intentionally deferred. The production execution strategy remains local under ADR-0010.

## Next concrete step

There is no remaining M22 implementation or acceptance work. Current non-terminal work is limited to the existing M00 maintenance items, M21 WI-0081 recognition-quality investigation, and the intentionally deferred M23 source-copy lifecycle/privacy work. Select the next priority from those existing items rather than reopening M22.

## Relevant files

- docs/delivery/milestones/M22-protected-smart-collection-slideshow.md
- docs/delivery/work-items/WI-0107-m22-slideshow-acceptance-gaps.md
- docs/delivery/status/work-items/archive/WI-0107.yaml
- docs/delivery/status/milestones.yaml
- docs/delivery/status/work-items-index.md
- src/PhotoIdentity.Web/Pages/Slideshows.razor.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationService.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationEndpoints.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowBrowserFormatPreparationApplicationTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
