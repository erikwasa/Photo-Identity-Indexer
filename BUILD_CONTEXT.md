# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**M20 — Operator polish and archive throughput** is the active delivery focus.

**WI-0073 — Polish cards, menus and archive navigation** is implemented and merged through PR #196 (`bb52ae10470262e6e7a0573a0b70671af207ca49`). **WI-0074 — Filter Face Review by suggested person** is implemented and merged through PR #197 (`1fa3a23d9a6b1aed916c752b4f6482e825668e7c`), with exact-head workflow #1218 (`32312949819`) green. Both remain `in_review`; the maintainer has explicitly deferred browser verification until multiple M20 items are ready for one consolidated pass.

**WI-0072 — Integrate archive photo metadata** was implemented and merged through PR #191, and **WI-0078 — Reprocess stale photo metadata after extraction-contract changes** was implemented and merged through PR #195. Their formal lifecycle state remains constrained by deferred real-media/provider verification rather than missing implementation. Do not mark them completed without maintainer evidence.

**WI-0075 — Make GeoNames background timing configurable from launcher settings** is implemented and merged through PR #198 (`1d00a7c533299bd5139c3a02ce3157dd52fec4c1`). Exact-head workflow #1221 (`32314889279`) passed build/tests, both integration shards, documentation checks, published review, mixed-media, launcher and package verification. WI-0075 remains formally `proposed` while WI-0065 awaits the deferred maintainer/provider verification.

**WI-0077 — Simplify Photo Viewer metadata and location editing** is the current implementation slice on `agent/WI-0077-photo-viewer-simplification`. The always-visible capture metadata is reduced to capture time, camera make/model and exact GPS coordinates. Lens/exposure/aperture/ISO/focal length/orientation/flash/GPS altitude move into collapsed `All metadata`, including structured values even when no matching raw tag exists. Location defaults to read-only Place display with one `Edit location` action; the existing Place picker/path and Set/Replace/Clear controls plus Cancel appear only in edit mode. Successful mutation returns to read mode.

WI-0077 remains formally `proposed` while WI-0072 awaits the deferred maintainer real-media/provider verification. Treat this as implementation against the already-merged prerequisite code; do not manufacture lifecycle completion solely to unblock implementation.

## Next concrete step

1. Validate WI-0077 exact-head CI; no API/schema/persistence changes are expected.
2. If green, record workflow evidence and mark its PR ready for merge without requesting the deferred browser review.
3. After merge, continue with another technically independent M20 implementation slice; archive throughput WI-0076 remains the largest remaining engineering item.
4. Keep WI-0072, WI-0073, WI-0074, WI-0075, WI-0077 and WI-0078 open until the maintainer initiates the consolidated acceptance pass.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0077-photo-viewer-simplification.md`
- `src/PhotoIdentity.Web/Components/PhotoMetadataPanel.razor`
- `src/PhotoIdentity.Web/Components/PhotoMetadataPanel.razor.css`
- `src/PhotoIdentity.Web/Components/PhotoPlaceEditor.razor`
- `src/PhotoIdentity.Web/Components/PhotoPlaceEditor.razor.css`
- `AGENTS.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
