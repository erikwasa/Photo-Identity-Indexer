# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0050 — Add photo metadata and persistent smart collections** is the active M19 implementation boundary.

WI-0056 hierarchical manual tags are complete. Automatic visible-content tagging remains deferred and WI-0049 is not part of the active M19 completion path. SQLite remains canonical and originals remain read-only.

PR #143 merged the WI-0050 capture-time/GPS foundation. The current Slice 2 adds safe local-only metadata backfill and a reusable smart-collection filter/query contract over canonical people, hierarchical tags, GPS bounds and photographic taken dates.

Maintainer verification is intentionally deferred until all non-deferred M19 implementation is complete so tags, capture metadata and saved smart collections can be reviewed together.

## Next concrete step

1. Complete Slice 2 automated validation: online-only metadata backfill must not hydrate or read the original; local metadata reads must verify the immutable revision hash first.
2. Validate the combined query contract: people and tags independently support `all|any`, zero people is valid, GPS/taken-time filters use capture metadata, and populated dimensions combine with AND semantics.
3. Merge Slice 2 after build/test/docs/review/package gates pass.
4. Start Slice 3: persist normalized smart-collection definitions in SQLite and add create/list/get/update/delete/query API operations.
5. Follow with the saved-collection UI, then perform the single integrated M19 maintainer verification pass.

## Relevant files

- `docs/delivery/work-items/WI-0050-exif-smart-collections.md`
- `docs/delivery/milestones/M19-library-intelligence.md`
- `docs/delivery/status/work-items.yaml`
- `src/PhotoIdentity.Core/Collections/SmartCollectionFilter.cs`
- `src/PhotoIdentity.Core/Sources/PhotoCaptureMetadata.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionQueryRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoMetadataBackfillRepository.cs`
- `src/PhotoIdentity.Api/PhotoMetadataBackfillService.cs`
- `src/PhotoIdentity.Api/SmartCollectionEndpoints.cs`
- `tests/PhotoIdentity.Integration.Tests/SmartCollectionQueryRepositoryTests.cs`
- `tests/PhotoIdentity.Integration.Tests/PhotoMetadataBackfillServiceTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
