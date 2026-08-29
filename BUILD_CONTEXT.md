# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0083 — Add stable Smart Collection slideshow snapshots** is implemented and in review on PR #216.

The slice adds a dedicated saved-collection slideshow snapshot operation rather than widening the existing paged Smart Collection workspace query. Exact-head workflow #1324 passed before the lifecycle-only closeout commit. Snapshot creation reads the saved definition and complete matching revision set inside one SQLite read transaction, then returns a lightweight deterministic oldest-to-newest revision-ID manifest. The normal workspace remains newest-first and limited to pages of at most 200 items.

WI-0082 remains `in_review`; its real trusted-LAN phone HTTPS/secure-context acceptance is intentionally deferred to the consolidated M22 review after the current M22 implementation items are complete.

WI-0076 remains separately recorded as `in_progress` and is not part of this M22 slice.

## Next concrete step

1. Merge PR #216 after the lifecycle-only status/evidence update remains green.
2. Begin WI-0084 fullscreen slideshow playback against the immutable snapshot contract.
3. Perform the consolidated M22 real-device/product review only after the current M22 work items are implemented.

## Relevant files

- `docs/delivery/work-items/WI-0083-slideshow-snapshot-manifest.md`
- `docs/delivery/milestones/M22-protected-smart-collection-slideshow.md`
- `docs/product/slideshow.md`
- `src/PhotoIdentity.Api/SmartCollectionEndpoints.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionQueryRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionRepository.cs`
- `tests/PhotoIdentity.Integration.Tests/SmartCollectionSlideshowSnapshotTests.cs`
- `docs/delivery/status/work-items.yaml`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
