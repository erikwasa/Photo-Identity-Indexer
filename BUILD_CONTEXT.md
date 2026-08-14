# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0050 — Add photo metadata and persistent smart collections** is the active M19 implementation boundary.

WI-0056 hierarchical manual tags are complete. Automatic visible-content tagging remains deferred and WI-0049 is not part of the active M19 completion path. SQLite remains canonical and originals remain read-only.

PR #143 merged the capture-time/GPS foundation. PR #144 merged explicit local-only metadata backfill plus the reusable smart-collection query contract. PR #145 merged persistent saved definitions and CRUD/saved-query APIs.

The current Slice 4 adds the saved-collection web workspace at `/smart-collections`. It uses canonical people and hierarchical tags, supports optional GPS bounds and taken-date shorthand, and keeps the existing `/collections` people/evidence workspace unchanged.

Maintainer verification remains intentionally deferred until all non-deferred M19 implementation is complete so tags, capture metadata and saved smart collections can be reviewed together.

## Next concrete step

1. Validate the Slice 4 Razor workspace builds and the `/smart-collections` route is exposed from primary navigation.
2. Validate create, reopen, edit, delete, preview and explicit saved-definition reevaluation against the merged Slice 3 APIs.
3. Validate people/tag `all|any`, date shorthand, GPS bounds, result pagination and photo-detail links through the web workspace.
4. Merge Slice 4 after build/test/docs/review/package/launcher gates pass.
5. Perform the single integrated M19 maintainer verification pass.
6. Record that human verification and complete WI-0050/M19 if no review defects remain.

## Relevant files

- `docs/delivery/work-items/WI-0050-exif-smart-collections.md`
- `docs/delivery/milestones/M19-library-intelligence.md`
- `docs/delivery/status/work-items.yaml`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor.cs`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor.css`
- `src/PhotoIdentity.Web/SmartCollectionContracts.cs`
- `src/PhotoIdentity.Web/Layout/MainLayout.razor`
- `src/PhotoIdentity.Api/SmartCollectionEndpoints.cs`
- `tests/PhotoIdentity.Integration.Tests/SmartCollectionWebRouteTests.cs`
- `tests/PhotoIdentity.Integration.Tests/SmartCollectionPersistenceTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
