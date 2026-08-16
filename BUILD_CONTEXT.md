# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0061 — Enrich Photo Details and preserve navigation context** is in its final implementation slice for M19.

Slices 1 and 2 merged through PR #150 and PR #152. Main also includes the separate PR #151 durable face-review derivative work and PR #153 integration-host stabilization; WI-0061 does not change the face-review derivative path.

Slice 3 now adds navigation restoration:

- saved Smart Collection URLs carry the saved definition identifier plus result offset and can reconstruct the workspace from catalogue state;
- transient unsaved previews store editor/filter state only in tab-scoped `sessionStorage`, referenced by a generated preview key plus result offset in the URL;
- Smart Collection photo links carry the current local workspace return URL into Photo Details;
- Photo Details validates the supplied return URL as a rooted local application route, labels Smart Collection returns appropriately and falls back to `/collections` for invalid or absent context;
- no navigation-state operation opens or hydrates an original image.

## Next concrete step

1. Validate Slice 3 build, full tests, living documentation and review smoke in GitHub Actions.
2. Merge the Slice 3 PR after CI and maintainer review.
3. Perform the WI-0061 local browser pass: browser/mouse Back from both a saved Smart Collection result page and an unsaved preview, plus the context-aware Photo Details Back control and safe fallback behavior.
4. After maintainer verification, mark WI-0061 completed and proceed with WI-0062 manual photo-level people. WI-0063 remains independently ready; WI-0064 follows WI-0063.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0061-photo-details-navigation-context.md`
- `src/PhotoIdentity.Web/NavigationContext.cs`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor.cs`
- `src/PhotoIdentity.Web/Pages/Photo.razor`
- `tests/PhotoIdentity.Integration.Tests/NavigationContextTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
