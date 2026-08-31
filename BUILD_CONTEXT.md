# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0094 — Add a read-only slideshow library with standalone original preparation** is active.

PR #224 / WI-0093 is merged on main, and post-merge workflow #1352 passed all lanes.

The implementation adds a dedicated `/slideshows` page using a ConsumerLayout with no ordinary operator navigation. The page receives only a read-only collection projection containing Smart Collection ID and name; it does not receive or render collection filters and exposes no edit/delete/photo-mutation actions.

The fullscreen slideshow and the consumer page share one SlideshowSettingsEditor component and the existing browser-local SlideshowSettings storage key. Manual navigation, orientation, autoplay, timing, progress, end behavior, protected mode and Prepare originals therefore remain one global browser profile.

Starting a slideshow from `/slideshows` passes that route as the return target. Slideshow return normalization now accepts `/slideshows` in addition to the Smart Collections operator workspace.

Standalone Prepare originals creates the existing immutable slideshow snapshot and feeds its revision IDs to the WI-0093 preparation API without entering fullscreen or starting playback. Progress, no-progress Retry and Cancel reuse the WI-0093 contract.

Active standalone preparation session IDs are stored browser-locally by collection. Navigating away cancels only client polling; it does not DELETE the server session, so preparation can continue in-process. Returning to the page reattaches to a still-live session where possible. When preparation becomes Ready, the page DELETEs the preparation session to release temporary slideshow protection while app-owned hydrated originals remain local under the normal managed LRU policy.

Per-collection client guards prevent repeated taps from creating duplicate preparation work in the same consumer page session.

The page is explicitly a read-only UI boundary, not authentication/authorization. Photo Identity remains unauthenticated on the trusted private network.

WI-0076 remains separately recorded as in_progress and is not part of this M22 slice.

## Next concrete step

1. Run exact-head CI for WI-0094 and correct any build/Razor/integration/package failures.
2. When green, record PR/workflow evidence and move WI-0094 to in_review.
3. Merge WI-0094 after lifecycle-only exact-head CI is green.
4. Repeat focused real-phone M22 acceptance for WI-0092 through WI-0094, then close M22 if it passes.

## Relevant files

- docs/delivery/work-items/WI-0094-read-only-slideshow-library.md
- docs/product/slideshow.md
- src/PhotoIdentity.Web/Layout/ConsumerLayout.razor
- src/PhotoIdentity.Web/Pages/Slideshows.razor
- src/PhotoIdentity.Web/Pages/Slideshows.razor.cs
- src/PhotoIdentity.Web/Components/SlideshowSettingsEditor.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor.cs
- src/PhotoIdentity.Web/SlideshowLibraryContracts.cs
- src/PhotoIdentity.Api/SmartCollectionEndpoints.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowWebRouteTests.cs
- docs/delivery/status/work-items.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
