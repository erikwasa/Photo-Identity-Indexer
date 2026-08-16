# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0061 — Enrich Photo Details and preserve navigation context** remains the active M19 work item.

Slice 1 merged through PR #150. Main also includes the separate PR #151 durable face-review derivative work; WI-0061 must not change that face-review storage/rendering path.

The active Slice 2 adds privacy-safe Photo Details metadata:

- expose the original file name as the source-key basename only, never the source root or relative directory;
- show canonical people backed by active confirmed face assignments only;
- exclude pending suggestions from the confirmed people list;
- keep the details query catalogue-only so it never opens or hydrates an original;
- reserve `ManualPresence` in the person response so WI-0062 can add face-independent photo/person presence without another browser-contract revision.

## Next concrete step

1. Validate Slice 2 build, integration tests and living documentation in GitHub Actions.
2. Merge Slice 2 after CI and maintainer review.
3. Finish WI-0061 with Slice 3: saved/transient Smart Collection state restoration and validated context-aware Back navigation.
4. After WI-0061 is complete, implement WI-0062 manual photo-level people. WI-0063 remains independently ready; WI-0064 follows WI-0063.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0061-photo-details-navigation-context.md`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoDetailsRepository.cs`
- `src/PhotoIdentity.Api/PhotoDetailsEndpoints.cs`
- `src/PhotoIdentity.Web/PhotoDetailsContracts.cs`
- `src/PhotoIdentity.Web/Pages/Photo.razor`
- `tests/PhotoIdentity.Integration.Tests/PhotoDetailsApplicationTests.cs`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
