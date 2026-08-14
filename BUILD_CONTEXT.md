# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0050 — Add photo metadata and persistent smart collections** is the active M19 implementation boundary.

WI-0056 hierarchical manual tags are complete. Automatic visible-content tagging remains deferred and WI-0049 is not part of the active M19 completion path. SQLite remains canonical and originals remain read-only.

PR #143 merged the capture-time/GPS foundation. PR #144 merged explicit local-only metadata backfill plus the reusable smart-collection query contract over people, hierarchical tags, GPS bounds and photographic taken dates.

The current Slice 3 persists named smart-collection definitions only. No static membership list is stored: saved evaluation loads the persisted filter and executes the same current-catalogue query implementation from Slice 2.

Maintainer verification remains intentionally deferred until all non-deferred M19 implementation is complete so tags, capture metadata and saved smart collections can be reviewed together.

## Next concrete step

1. Validate Slice 3 persistence: canonical names, versioned normalized filter JSON, duplicate-name conflict handling and CRUD round trips.
2. Validate saved collection reevaluation: add a newly matching photo after save and confirm the saved query includes it without changing the definition.
3. Validate create/list/get/update/delete and `/api/smart-collections/{id}/query` through the application API.
4. Merge Slice 3 after build/test/docs/review/package/launcher gates pass.
5. Start Slice 4: saved-collection UI for create, edit, reopen and evaluate.
6. Perform the single integrated M19 maintainer verification pass after the UI slice.

## Relevant files

- `docs/delivery/work-items/WI-0050-exif-smart-collections.md`
- `docs/delivery/milestones/M19-library-intelligence.md`
- `docs/delivery/status/work-items.yaml`
- `src/PhotoIdentity.Core/Collections/SmartCollectionFilter.cs`
- `src/PhotoIdentity.Core/Collections/SmartCollectionDefinition.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionQueryRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionRepository.cs`
- `src/PhotoIdentity.Api/SmartCollectionEndpoints.cs`
- `tests/PhotoIdentity.Integration.Tests/SmartCollectionQueryRepositoryTests.cs`
- `tests/PhotoIdentity.Integration.Tests/SmartCollectionPersistenceTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
