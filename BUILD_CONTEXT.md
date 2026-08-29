# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0084 — Build fullscreen Smart Collection slideshow playback** is implemented and in review on PR #217.

The slice builds the core presentation lifecycle on WI-0083's immutable saved-collection snapshot. Exact-head workflow #1328 passed before the lifecycle-only closeout commit: Start slideshow requests browser fullscreen before navigation/network work, the slideshow route loads the stable manifest, normal pixels use the existing non-hydrating viewer-preview endpoint, and only the current image plus a one-item previous/next prefetch window are retained by slideshow-owned browser image objects.

Playback/settings behavior is isolated into testable C# state: autoplay waits for image readiness, pause/hidden-document states freeze the timer, manual navigation resets timing after the destination image is ready, and loop/stop/exit end behavior follows the M22 contract. Browser-local settings include the complete V1 object, including Protected slideshow and Prepare originals for WI-0085/WI-0086 to consume.

WI-0082 and WI-0083 remain `in_review` because the maintainer explicitly deferred consolidated M22 acceptance until all current M22 implementation items are complete. WI-0083 is already merged and is therefore an implementation dependency satisfied for WI-0084.

WI-0076 remains separately recorded as `in_progress` and is not part of this M22 slice.

## Next concrete step

1. Merge PR #217 after the lifecycle-only status/evidence update remains green.
2. Begin WI-0085 toddler-safe fullscreen/orientation/wake hardening.
3. Perform the consolidated M22 real-device/product review only after the current M22 work items are implemented.

## Relevant files

- `docs/delivery/work-items/WI-0084-fullscreen-slideshow-playback.md`
- `docs/product/slideshow.md`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor`
- `src/PhotoIdentity.Web/Pages/Slideshow.razor`
- `src/PhotoIdentity.Web/SlideshowPlaybackState.cs`
- `src/PhotoIdentity.Web/SlideshowSettings.cs`
- `src/PhotoIdentity.Web/wwwroot/js/slideshow.js`
- `tests/PhotoIdentity.Integration.Tests/SlideshowPlaybackStateTests.cs`
- `tests/PhotoIdentity.Integration.Tests/SlideshowSettingsTests.cs`
- `tests/PhotoIdentity.Integration.Tests/SlideshowWebRouteTests.cs`
- `docs/delivery/status/work-items.yaml`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
