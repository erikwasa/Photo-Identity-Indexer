# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

The original M19 baseline is verified and complete at the WI-0050/WI-0056 boundary. The maintainer completed the integrated local review on 2026-08-16 and reported that M19 and the implemented work-item functions behaved as expected.

M19 has now been extended with four follow-up work items. No follow-up implementation has started yet:

- **WI-0061 — Enrich Photo Details and preserve navigation context**: original filename, consolidated confirmed people and return-state restoration from Smart Collections.
- **WI-0062 — Add manual photo-level people**: revision-level person presence independent of face detection/identification, feeding Smart Collections but not face evidence.
- **WI-0063 — Make Places a first-class location hierarchy**: reserve `Places/`, enforce one effective place, separate Places from generic tags and add hierarchical named-place filtering to Location.
- **WI-0064 — Add GeoNames reverse-geocoded Places enrichment**: use the GeoNames web-service API from persisted GPS, with no downloaded GeoNames database extracts.

Automatic visible-content tagging remains deferred and WI-0049 is not part of the active M19 completion path. SQLite remains canonical and originals remain read-only.

## Dependency shape

Two work items are immediately ready and can proceed independently:

1. WI-0061 (Photo Details/navigation context).
2. WI-0063 (first-class Places hierarchy).

WI-0062 depends on WI-0061 so its manual people controls can reuse the consolidated Photo Details contract. WI-0064 depends on WI-0063 so GeoNames writes into a settled single-place/location model rather than defining that model itself.

## Next concrete step

1. Merge the documentation/status change that registers WI-0061 through WI-0064 and closes the verified WI-0050 baseline.
2. Choose either WI-0061 or WI-0063 as the first implementation track; they are intentionally parallel-ready.
3. Implement WI-0062 after WI-0061 is merged.
4. Implement WI-0064 after WI-0063 is merged, using the GeoNames HTTPS web-service API and private local username configuration.
5. After all four follow-up work items are complete, perform one integrated M19 extension verification pass.

## Relevant files

- `docs/delivery/milestones/M19-library-intelligence.md`
- `docs/delivery/status/milestones.yaml`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0050-exif-smart-collections.md`
- `docs/delivery/work-items/WI-0061-photo-details-navigation-context.md`
- `docs/delivery/work-items/WI-0062-manual-photo-people.md`
- `docs/delivery/work-items/WI-0063-first-class-places.md`
- `docs/delivery/work-items/WI-0064-geonames-place-enrichment.md`
- `src/PhotoIdentity.Web/Pages/Photo.razor`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionQueryRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoTagRepository.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
