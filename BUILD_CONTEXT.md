# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence live in `docs/delivery/status/work-items.yaml`. Durable product, architecture and operating decisions belong in the linked documentation rather than being repeated here.

## Current focus

**WI-0055 — Fix packaged review, archive and storage-policy regressions** is the current verification boundary.

The packaged-runtime implementation has been merged to `main` through PRs #133, #134 and #135. The merged work covers package-local model manifests and governed model weights, the Library local-original preview fallback, review-proxy launcher settings, bounded-hydration launcher settings, and Settings storage telemetry.

Formal completion still requires the human Windows verification defined by WI-0055. A merged implementation is not completion evidence by itself.

## Next concrete step

Run WI-0055 verification from the packaged Windows application against the maintained durable configuration without forcing mass re-analysis or uncontrolled hydration:

1. Confirm Face Gallery and Face Details prefer a materially higher-resolution human-review image when the configured durable proxy/profile is available, rather than scaling the 112x112 recognition crop.
2. Confirm existing permanent-archive Analyzed/Pending/Failed counts are meaningful and unchanged completed analysis remains reusable.
3. Confirm **Advance archive** works without a source checkout and backfills missing durable proxies where appropriate.
4. Confirm Library preview behavior is coherent for both already-local revision-verified originals and online-only originals, with no implicit hydration from normal browsing.
5. Confirm Settings reports the configured hydration policy, current free space, managed hydration usage/budget and selected review-proxy profile.

If verification passes, record human evidence and complete WI-0055 through the normal delivery-status workflow. If a defect remains, keep the fix scoped to WI-0055 and repeat only the affected verification checks plus the packaged-runtime gate.

## Relevant files

- `docs/delivery/work-items/WI-0055-packaged-runtime-regressions.md`
- `docs/delivery/status/work-items.yaml`
- `docs/product/success-criteria.md`
- `docs/operations/local-operator-guide.md`
- `docs/operations/windows-package.md`
- `docs/operations/bounded-archive-acceptance.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./verify-review.ps1 -Mode Smoke -Configuration Release
./verify-launcher.ps1 -Configuration Release
./Package-PhotoIdentity.ps1 -Configuration Release
./verify-package.ps1 -Configuration Release
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```
