# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0063 — Make Places a first-class location hierarchy** is the active M19 implementation item.

WI-0061 and WI-0062 implementation are merged. Their local browser verification is intentionally deferred and will be performed together with WI-0063/WI-0064 in the consolidated M19 maintainer pass.

Slice 1 is in draft PR #156 and establishes the first-class Places foundation:

- reserve `Places`/`Places/...` so ordinary tag routes reject and hide the namespace;
- reuse canonical hierarchical `photo_tags` vocabulary while storing one effective revision-level place in append-only `photo_place_actions`;
- expose dedicated place vocabulary/state/set/replace/clear APIs without the literal `Places/` prefix in normal values;
- migrate coherent legacy Places chains to the deepest node while surfacing divergent paths in `photo_place_migration_conflicts`;
- keep all place operations catalogue-only with no original access or hydration.

The current `smart_collections` table hard-constrains filter schema version 1. The formal catalogue schema migration is therefore intentionally paired with the Smart Collection v2 table rebuild in Slice 2 rather than forcing two adjacent migrations.

## Next concrete step

1. Validate draft PR #156 build, integration tests, living documentation, review smoke and Windows verification in GitHub Actions.
2. Merge Slice 1 after automated validation and code review.
3. Implement WI-0063 Slice 2: Smart Collection named-place Location contract, exact ancestor matching, Places exclusion from generic Smart tags, saved-filter v1→v2 migration and formal M19 schema migration.
4. Implement Slice 3: Photo Details place editor and Smart Collection hierarchical place UI.
5. Implement WI-0064 GeoNames enrichment, then perform the consolidated M19 browser/operator verification.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0063-first-class-places.md`
- `src/PhotoIdentity.Core/Places/PhotoPlacePath.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoPlaceSchema.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoPlaceRepository.cs`
- `src/PhotoIdentity.Api/PhotoPlaceEndpoints.cs`
- `src/PhotoIdentity.Api/PhotoTagEndpoints.cs`
- `tests/PhotoIdentity.Integration.Tests/PhotoPlaceFoundationApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
