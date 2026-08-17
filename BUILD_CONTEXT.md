# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0063 — Make Places a first-class location hierarchy** is the active M19 implementation item.

WI-0061 and WI-0062 implementation are merged. Their local browser verification is intentionally deferred and will be performed together with WI-0063/WI-0064 in the consolidated M19 maintainer pass.

Slice 1 merged through PR #156 at `2c67f7ae653c441b6ce11e79e89d3f9c38d7aef2` and established the first-class Places persistence/API boundary.

Slice 2 is in draft PR #158 on `agent/WI-0063-smart-place-location`:

- Smart Collection filter schema v2 adds an optional canonical named place inside Location while preserving optional GPS bounds;
- existing v1 saved definitions remain readable, while new or edited definitions persist as v2;
- the `smart_collections` table constraint is rebuilt compatibly from v1-only to v1/v2 when needed;
- generic Smart Collection tags exclude the reserved `Places` subtree at both Core and query boundaries;
- named-place matching uses the selected canonical node plus descendants only, never global leaf-name matching;
- named place and GPS bounds combine with AND semantics and continue composing with people, generic tags and taken time;
- integration coverage exercises ancestor matching, duplicate locality names, combined GPS/place criteria and v1/v2 persistence.

The catalogue-wide `PRAGMA user_version` marker has not yet been bumped because the connected GitHub contents API only supports whole-file replacement for the large bootstrap file. Persisted correctness does not depend on that marker; the M19 tables remain idempotently materialized. Fold the M19 lazy tables into the normal catalogue migration once a safe patch path for that file is available.

## Next concrete step

1. Validate draft PR #158 build, integration tests, living documentation, review smoke and Windows verification in GitHub Actions.
2. Merge Slice 2 after automated validation and code review.
3. Implement WI-0063 Slice 3: Photo Details place editor, migration-conflict presentation and Smart Collection hierarchical place UI.
4. Implement WI-0064 GeoNames enrichment.
5. Perform the consolidated local M19 browser/operator verification, then close the remaining in-review items as appropriate.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0063-first-class-places.md`
- `src/PhotoIdentity.Core/Collections/SmartCollectionFilter.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionQueryRepository.cs`
- `src/PhotoIdentity.Api/SmartCollectionEndpoints.cs`
- `src/PhotoIdentity.Web/SmartCollectionContracts.cs`
- `tests/PhotoIdentity.Integration.Tests/SmartCollectionPlaceApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
