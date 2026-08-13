# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0056 — Add canonical photo tags and manual tagging** is the current maintainer-verification boundary for M19.

The implementation is merged through PR #138 and is now in review. It establishes canonical case-insensitive photo tags, revision-bound auditable manual add/remove history, revision-scoped API endpoints and photo-viewer fallback/correction controls. Manual tagging is a recovery path; automatic visible-content tagging remains the intended primary M19 workflow.

WI-0057 is complete. The former monolithic work-item registry is preserved under `docs/delivery/status/archive/`, while the small current registry remains the normal update surface. Automatic archive rotation is not planned at this time.

## Next concrete step

Complete maintainer verification for WI-0056, then start WI-0049 as the primary automatic-tagging investigation:

1. Open representative photos through `/photo/{RevisionId}` and confirm existing manual fallback tags load without changing original availability.
2. Add a fallback/correction tag, reload the page and confirm it persists with stable display spelling; adding the same tag with different casing or whitespace must not create a duplicate.
3. Remove and re-add a tag, including a representative free-form name containing `/`, and confirm the interaction remains clear on desktop and Pixel-sized layouts.
4. On an online-only original, confirm manual tag add/remove does not request hydration and does not modify the source file.
5. If verification passes, record human evidence and complete WI-0056.
6. Start WI-0049 with automatic tagging explicitly treated as the normal/default path. Compare review-proxy versus original input and determine the production model, evidence, threshold and manual-override boundary.

## Relevant files

- `docs/delivery/work-items/WI-0056-manual-photo-tags.md`
- `docs/delivery/milestones/M19-library-intelligence.md`
- `docs/delivery/work-items/WI-0049-visible-content-tagging-experiment.md`
- `docs/delivery/work-items/WI-0050-exif-smart-collections.md`
- `docs/delivery/status/work-items.yaml`
- `src/PhotoIdentity.Core/Tags/PhotoTagName.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoTagRepository.cs`
- `src/PhotoIdentity.Api/PhotoTagEndpoints.cs`
- `src/PhotoIdentity.Web/PhotoTagContracts.cs`
- `src/PhotoIdentity.Web/Pages/Photo.razor`
- `tests/PhotoIdentity.Integration.Tests/PhotoTagApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
