# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0061 — Enrich Photo Details and preserve navigation context** is the active M19 work item.

The original M19 WI-0050/WI-0056 baseline was verified by the maintainer on 2026-08-16. M19 is now extended by WI-0061 through WI-0064. Automatic visible-content tagging remains deferred; SQLite stays canonical and originals remain read-only.

WI-0061 is being delivered in three slices:

1. Photo viewer/original-state cleanup.
2. Photo detail metadata (original filename and confirmed people).
3. Smart Collection navigation-state restoration and context-aware Back navigation.

The active Slice 1 changes Photo Details so an already-local, revision-verified original is displayed directly instead of a review proxy. Online-only originals continue to use a durable proxy when available without implicit hydration. After explicit hydration completes, the same Photo Details view switches to the original rather than downloading it or opening another tab. Original availability is shown once as a badge, with one `Managed by: Photo Identity|OneDrive|No` value.

## Next concrete step

1. Validate Slice 1 build/tests/docs and viewer integration tests.
2. Merge Slice 1 after CI passes and maintainer review.
3. Continue WI-0061 with privacy-safe original filename and consolidated confirmed-people detail data.
4. Finish WI-0061 with saved/transient Smart Collection navigation restoration and validated context-aware Back navigation.
5. Implement WI-0062 after WI-0061 is complete. WI-0063 remains independently ready; WI-0064 follows WI-0063.

## Relevant files

- `docs/delivery/milestones/M19-library-intelligence.md`
- `docs/delivery/status/milestones.yaml`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0061-photo-details-navigation-context.md`
- `src/PhotoIdentity.Api/CollectionViewerPreviewEndpoints.cs`
- `src/PhotoIdentity.Api/CollectionEndpoints.cs`
- `src/PhotoIdentity.Web/Pages/Photo.razor`
- `src/PhotoIdentity.Web/Pages/Photo.razor.css`
- `tests/PhotoIdentity.Integration.Tests/CollectionViewerPreviewApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
