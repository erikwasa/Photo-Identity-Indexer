# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0085 — Harden slideshow playback for toddler-safe phone use** is the active M22 implementation item.

The slice layers toddler-safe protection onto the merged WI-0084 playback shell without changing WI-0086 original-preparation behavior. Protected slideshow remains the default. Normal protected playback hides administrative chrome and exposes parent controls only after the two-corner hold gesture or the desktop `Ctrl+Shift+X` shortcut. Exit from protected mode requires a second press-and-hold confirmation.

Browser Back is guarded through Blazor navigation interception, unexpected fullscreen loss pauses playback on the existing black slideshow shell, and explicit fullscreen recovery reacquires phone protections. Browser feature checks are centralized in `slideshow.js`: fullscreen, exact/family orientation lock, screen wake lock and secure-context state are reported independently as support/acquisition status. Wake lock is reacquired after visibility return and orientation/wake ownership is released on deliberate slideshow exit.

Rapid toddler navigation is coalesced through a one-pending-action gate rather than allowing an unbounded queue of image transitions. Hidden unlock zones are inset from CSS safe-area insets, while image drag/context-menu/selection and browser gesture suppression remain on the presentation surface.

WI-0082, WI-0083 and WI-0084 remain `in_review` because consolidated real-device/product acceptance is intentionally deferred until the current M22 implementation items are complete. Their implementation dependencies are merged and satisfied.

WI-0076 remains separately recorded as `in_progress` and is not part of this M22 slice.

## Next concrete step

1. Run exact-head CI for WI-0085 and correct build/test/documentation failures.
2. After CI is green, record PR/workflow evidence and move WI-0085 to `in_review`.
3. Merge WI-0085, then begin WI-0086 slideshow original preparation.
4. Perform the consolidated M22 real-device/product review only after WI-0086 is implemented.

## Relevant files

- `docs/delivery/work-items/WI-0085-protected-toddler-slideshow.md`
- `docs/product/slideshow.md`
- `src/PhotoIdentity.Web/Pages/Slideshow.razor`
- `src/PhotoIdentity.Web/Pages/Slideshow.razor.cs`
- `src/PhotoIdentity.Web/Pages/Slideshow.razor.css`
- `src/PhotoIdentity.Web/SlideshowProtection.cs`
- `src/PhotoIdentity.Web/SlideshowNavigationGate.cs`
- `src/PhotoIdentity.Web/wwwroot/js/slideshow.js`
- `tests/PhotoIdentity.Integration.Tests/SlideshowProtectionStateTests.cs`
- `tests/PhotoIdentity.Integration.Tests/SlideshowNavigationGateTests.cs`
- `docs/delivery/status/work-items.yaml`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
