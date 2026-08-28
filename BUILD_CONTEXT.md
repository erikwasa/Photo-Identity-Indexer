# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0082 — Add secure trusted-LAN phone access for slideshow use** is the active M22 item.

The maintainer explicitly prioritized implementation of the current M22 slideshow work items on 2026-08-28 and asked to defer the full milestone review until all current M22 items are implemented. This slice preserves the existing loopback-only desktop URL and adds a separate opt-in HTTPS mobile listener using one explicit non-loopback IP address, an operator-owned PFX and an optional advertised phone URL.

WI-0076 remains recorded as `in_progress` and still needs formal closeout against its already collected benchmark evidence. That closeout is not part of this M22 slice.

## Next concrete step

1. Run the required launcher/package CI for the WI-0082 branch and correct any validation failures.
2. After merge, perform WI-0082 maintainer verification on the real trusted-LAN phone: certificate trust, valid HTTPS, same-origin UI/images/API resources and `window.isSecureContext === true`.
3. Then begin WI-0083, the immutable complete Smart Collection slideshow snapshot contract.

## Relevant files

- `docs/delivery/work-items/WI-0082-secure-mobile-slideshow-access.md`
- `docs/delivery/milestones/M22-protected-smart-collection-slideshow.md`
- `docs/product/slideshow.md`
- `Start-PhotoIdentity.ps1`
- `verify-launcher.ps1`
- `docs/operations/windows-package.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Repository validation

```powershell
./build.ps1
./test.ps1
./verify-launcher.ps1 -Configuration Release
./verify-package.ps1 -Configuration Release
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```
