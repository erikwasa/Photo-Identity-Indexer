# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0056 — Add Immich-compatible hierarchical manual photo tags** is the current M19 implementation boundary.

Automatic visible-content tagging is on hold and WI-0049 is no longer part of the active M19 completion path. Manual tags remain supported and now use slash-separated hierarchical values, while SQLite remains the canonical store and original photos remain read-only.

M19 then proceeds to **WI-0050 — Add photo metadata and persistent smart collections**. Smart collections will persist filter definitions over people, hierarchical tags, GPS/location criteria and photographic taken-time, and will reevaluate against the current catalogue so newly matching photos appear automatically.

WI-0057 is complete. Automatic work-item archive rotation is not planned at this time.

## Next concrete step

1. Finish WI-0056 hierarchy compatibility: validate root and nested tags, case/whitespace normalization, parent vocabulary, add/remove audit history and the canonical `/api/tags` contract.
2. Run automated build/test/documentation gates.
3. Perform maintainer verification in the photo viewer, including an online-only original to confirm tag edits do not hydrate or modify it.
4. Complete WI-0056 and start WI-0050.
5. Implement WI-0050 in bounded slices: capture-time/GPS persistence, combined query contract, saved smart-collection CRUD/query API, then UI.

## Relevant files

- `docs/delivery/work-items/WI-0056-manual-photo-tags.md`
- `docs/delivery/work-items/WI-0050-exif-smart-collections.md`
- `docs/delivery/milestones/M19-library-intelligence.md`
- `docs/delivery/status/work-items.yaml`
- `src/PhotoIdentity.Core/Tags/PhotoTagName.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoTagRepository.cs`
- `src/PhotoIdentity.Api/PhotoTagEndpoints.cs`
- `src/PhotoIdentity.Web/PhotoTagContracts.cs`
- `tests/PhotoIdentity.Integration.Tests/PhotoTagApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
