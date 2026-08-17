# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0063 — Make Places a first-class location hierarchy** is the active M19 implementation item.

WI-0061 and WI-0062 implementation are merged. Their local browser verification is intentionally deferred and will be performed together with WI-0063/WI-0064 in the consolidated M19 maintainer pass.

Slice 1 merged through PR #156 and established the first-class Places persistence/API foundation. Slice 2 is in draft PR #157 on `agent/WI-0063-smart-location` and adds Smart Collection Location semantics:

- Location can contain one canonical named place and optional GPS bounds; both predicates must match when both are supplied;
- named-place matching is exact canonical hierarchy ancestry, never global leaf-text matching;
- new Smart Collection requests reject the reserved Places hierarchy in Tags, while one legacy saved Places tag can migrate into Location without silent loss;
- saved-filter schema v2 preserves coordinate-only v1 JSON compatibility;
- SQLite schema v14 formalizes the M19 lazy capture-metadata, smart-collection, manual-person and first-class-place tables and promotes existing saved definitions to filter schema v2;
- focused integration tests cover ancestry, duplicate locality names, cross-dimension composition, API reservation and v13/v1 migration.

All place and Smart Collection operations remain catalogue-only and do not open or hydrate originals.

## Next concrete step

1. Validate draft PR #157 build, integration tests, living documentation, review smoke and Windows verification in GitHub Actions.
2. Merge Slice 2 after automated validation and code review.
3. Implement WI-0063 Slice 3: Photo Details place editor and Smart Collection hierarchical place UI while preserving WI-0061 navigation state.
4. Implement WI-0064 GeoNames enrichment.
5. Perform the consolidated M19 browser/operator verification and close the remaining in-review work items as appropriate.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0063-first-class-places.md`
- `src/PhotoIdentity.Core/Collections/SmartCollectionFilter.cs`
- `src/PhotoIdentity.Core/Places/PhotoPlacePath.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionQueryRepository.cs`
- `src/PhotoIdentity.Api/SmartCollectionEndpoints.cs`
- `src/PhotoIdentity.Web/SmartCollectionContracts.cs`
- `tests/PhotoIdentity.Integration.Tests/SmartCollectionPlaceLocationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
