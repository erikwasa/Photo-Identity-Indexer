# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence live in `docs/delivery/status/work-items.yaml`. Durable product, architecture and operating decisions belong in the linked documentation rather than being repeated here.

## Current focus

**WI-0056 — Add canonical photo tags and manual tagging** is the current implementation boundary for M19.

Slice 1 establishes the production tag contract before automatic tagging is selected: canonical case-insensitive tag identity, revision-bound append-only manual add/remove history, a separate model-evidence table, revision-scoped API endpoints and integration coverage. Manual assignments and future model evidence are intentionally separate so rerunning a model cannot overwrite maintainer intent.

M19 sequencing is now WI-0056 → WI-0049 for automatic visible-content experimentation, while WI-0050 can consume canonical manual tags independently of whether an automatic model is selected.

## Next concrete step

Validate the Slice 1 branch through CI, then implement Slice 2 on the same WI-0056 branch:

1. Add manual tag controls to `/photo/{RevisionId}` using the new tag API without requiring original hydration.
2. Show current manual tags clearly and support add/remove with useful validation feedback.
3. Add end-to-end application coverage for the photo-viewer workflow.
4. Run the normal repository/documentation validation before moving WI-0056 to review.

## Relevant files

- `docs/delivery/work-items/WI-0056-manual-photo-tags.md`
- `docs/delivery/milestones/M19-library-intelligence.md`
- `docs/delivery/work-items/WI-0049-visible-content-tagging-experiment.md`
- `docs/delivery/work-items/WI-0050-exif-smart-collections.md`
- `src/PhotoIdentity.Core/Tags/PhotoTagName.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoTagRepository.cs`
- `src/PhotoIdentity.Api/PhotoTagEndpoints.cs`
- `src/PhotoIdentity.Web/Pages/Photo.razor`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```
