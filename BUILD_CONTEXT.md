# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0066 — Add Smart Collection visibility preference for people** is the active M19 implementation item.

Slice 1 merged through PR #170. It establishes schema v16, a narrowly scoped durable `HiddenFromSmartCollections` preference, maintenance API read/write contracts, deterministic target-wins merge semantics and integration coverage. Hidden people remain part of ordinary review/identity people lists; Smart Collection discovery filtering is intentionally a later slice.

WI-0065 implementation merged through PR #166 and is in review pending maintainer verification of unattended pickup and restart/resume behavior.

WI-0069 is complete. Its merged CI optimization reuses successful build/test/documentation work for mixed-media verification and cancels superseded pull-request runs.

## Next concrete step

1. Implement WI-0066 Slice 2: add the reversible hide/unhide control and status indicator to Maintain People.
2. Implement Slice 3: filter normal Smart Collection people discovery while preserving and marking hidden people already referenced by saved definitions.
3. Run the focused maintainer browser pass for WI-0066 before moving to WI-0067.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0066-smart-collection-person-visibility.md`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePersonSmartCollectionVisibilityRepository.cs`
- `src/PhotoIdentity.Api/PersonMaintenanceEndpoints.cs`
- `src/PhotoIdentity.Web/ReviewContracts.cs`
- `src/PhotoIdentity.Web/Pages/People.razor`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor.cs`
- `tests/PhotoIdentity.Integration.Tests/PersonSmartCollectionVisibilityApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
