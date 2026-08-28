# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0080 — Make the detected face unambiguous in review images** is the active corrective engineering item.

PR #209 merged the approved dynamic **Target** overlay, but the 2026-08-28 real-catalogue verification failed: no overlay appeared in Face Review or Face Details, and the tested catalogue showed no persisted Photo dimensions. The permanent archive paths historically leave `asset_revisions.width`/`height` null, while PR #209's regression seed supplied explicit dimensions.

The active branch is `agent/WI-0080-existing-catalogue-target-overlay`. It keeps existing analysis and derivatives intact, carries revision identity into review records, prefers true photo dimensions when present, and otherwise uses the configured whole-photo review proxy's persisted dimensions only as an aspect-ratio geometry surrogate for normalized face observations. Proxy dimensions must not be exposed as original Photo dimensions.

WI-0076 session reuse merged through PR #212 and remains separate throughput acceptance work. WI-0081 remains ready but should not start until WI-0080 real-catalogue acceptance is resolved.

## Next concrete step

1. Complete the WI-0080 existing-catalogue corrective implementation and required CI.
2. Package the exact corrective head/current main after merge; do **not** re-analyze or regenerate existing faces.
3. Open Face Review and Face Details against the same permanent catalogue that previously showed no overlay.
4. Confirm every face with usable persisted geometry now shows the **Target** outline/label, including a contextual image containing another visible face where available.
5. Confirm Photo dimensions remain `—` when true original dimensions are unknown and that no original hydration occurs merely to render target geometry.
6. If the visual treatment is unmistakable, record maintainer verification and complete WI-0080; otherwise keep the item open with the observed case.

## Relevant files

- `docs/delivery/work-items/WI-0080-detected-face-clarity.md`
- `src/PhotoIdentity.Api/ReviewFaceTargetResolver.cs`
- `src/PhotoIdentity.Api/ReviewFacePreviewResolver.cs`
- `src/PhotoIdentity.Api/ReviewEndpoints.cs`
- `src/PhotoIdentity.Api/SuggestionGalleryEndpoints.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteArchiveReviewProxyRepository.cs`
- `tests/PhotoIdentity.Integration.Tests/ReviewFaceDetailImageApplicationTests.cs`
- `docs/delivery/status/work-items.yaml`
- `AGENTS.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
