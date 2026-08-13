# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence live in `docs/delivery/status/work-items.yaml`. Durable product, architecture and operating decisions belong in the linked documentation rather than being repeated here.

## Current focus

**WI-0056 — Add canonical photo tags and manual tagging** is the current verification boundary for M19.

PR #138 now implements the production manual-tag foundation end to end: canonical case-insensitive tag identity, revision-bound append-only add/remove history, revision-scoped API endpoints, Web contracts, photo-viewer controls and application/integration coverage. Manual persistence intentionally contains no model score/confidence schema; WI-0049 must first establish complete automatic inference-pipeline provenance and output shape.

M19 sequencing is WI-0056 → WI-0049 for automatic visible-content experimentation, while WI-0050 can consume canonical manual tags independently of whether an automatic model is selected.

## Next concrete step

Complete CI and maintainer verification for WI-0056 before starting WI-0049:

1. Open representative photos through `/photo/{RevisionId}` and confirm existing manual tags load without changing original availability.
2. Add a tag, reload the page and confirm the tag persists with stable display spelling; adding the same tag with different casing/whitespace must not create a duplicate.
3. Remove and re-add a tag, including a representative free-form name containing `/`, and confirm the interaction remains clear on desktop and Pixel-sized layouts.
4. On an online-only original, confirm tag add/remove does not request hydration and does not modify the source file.
5. If verification passes, record evidence and complete WI-0056. Then start WI-0049 with the review-proxy-versus-original controlled-vocabulary experiment boundary already documented.

## Relevant files

- `docs/delivery/work-items/WI-0056-manual-photo-tags.md`
- `docs/delivery/milestones/M19-library-intelligence.md`
- `docs/delivery/work-items/WI-0049-visible-content-tagging-experiment.md`
- `docs/delivery/work-items/WI-0050-exif-smart-collections.md`
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
