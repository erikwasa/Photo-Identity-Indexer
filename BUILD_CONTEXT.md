# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**WI-0094 — Add a read-only slideshow library with standalone original preparation** is implemented and in review on PR #225.

The implementation adds a dedicated `/slideshows` page using a ConsumerLayout with no ordinary operator navigation. The page receives only a read-only collection projection containing Smart Collection ID and name; it does not receive or render collection filters and exposes no edit/delete/photo-mutation actions.

The fullscreen slideshow and the consumer page share one SlideshowSettingsEditor component and the existing browser-local SlideshowSettings storage key. Manual navigation, orientation, autoplay, timing, progress, end behavior, protected mode and Prepare originals therefore remain one global browser profile.

Starting a slideshow from `/slideshows` passes that route as the return target. Slideshow return normalization accepts `/slideshows` in addition to the Smart Collections operator workspace.

Standalone Prepare originals creates the existing immutable slideshow snapshot and feeds its revision IDs to the WI-0093 preparation API without entering fullscreen or starting playback. Progress, no-progress Retry and Cancel reuse the WI-0093 contract.

Active standalone preparation session IDs are stored browser-locally by collection. Navigating away cancels only client polling; it does not DELETE the server session, so preparation can continue in-process. Returning to the page reattaches to a still-live session where possible. When preparation becomes Ready, the page DELETEs the preparation session to release temporary slideshow protection while app-owned hydrated originals remain local under the normal managed LRU policy.

Per-collection client guards prevent repeated taps from creating duplicate preparation work in the same consumer page session.

The page is explicitly a read-only UI boundary, not authentication/authorization. Photo Identity remains unauthenticated on the trusted private network.

Exact-head workflow #1353 passed all lanes: build-and-test, both integration shards, launcher verification and package verification.

WI-0076 remains separately recorded as in_progress and is not part of this M22 slice.

## Next concrete step

1. Wait for lifecycle-only CI on the current PR #225 head to complete.
2. If green and no review blockers exist, merge PR #225.
3. Repeat focused real-phone M22 acceptance for WI-0092 through WI-0094.
4. If that passes, record maintainer evidence and close WI-0082 through WI-0086 plus WI-0092 through WI-0094 and M22.

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
