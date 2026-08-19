# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**M20 — Operator polish and archive throughput** is the active delivery focus.

**WI-0073 — Polish cards, menus and archive navigation** is implemented and merged through PR #196 (`bb52ae10470262e6e7a0573a0b70671af207ca49`). **WI-0074 — Filter Face Review by suggested person** is implemented and merged through PR #197 (`1fa3a23d9a6b1aed916c752b4f6482e825668e7c`), with exact-head workflow #1218 (`32312949819`) green. Both remain `in_review`; the maintainer has explicitly deferred browser verification until multiple M20 items are ready for one consolidated pass.

**WI-0072 — Integrate archive photo metadata** was already implemented and merged through PR #191, and **WI-0078 — Reprocess stale photo metadata after extraction-contract changes** was already implemented and merged through PR #195. Their formal lifecycle state remains constrained by deferred real-media/provider verification rather than missing implementation. Do not mark them completed without maintainer evidence.

**WI-0075 — Make GeoNames background timing configurable from launcher settings** is the current implementation slice in PR #198 on `agent/WI-0075-geonames-timing-settings`. The launcher accepts the three automatic-enrichment settings, rejects request pacing below the existing 30000 ms safety floor with an explicit configuration error, accepts idle polling from 1000 through 600000 ms, and passes non-default values to the API. `/api/place-enrichment/status` exposes both effective automatic intervals. Launcher/package examples and operator documentation describe defaults, units, restart behavior and provider-backoff precedence.

WI-0075 remains formally `proposed` while WI-0065 awaits the deferred maintainer/provider verification. Treat PR #198 as an implementation-only stacked slice against the already-merged WI-0065/WI-0072 code; do not manufacture a lifecycle completion solely to unblock implementation.

## Next concrete step

1. Validate PR #198 exact-head CI, especially Windows launcher and package verification.
2. If green, record workflow evidence in PR #198 and mark it ready for review without merging it.
3. Continue implementing technically independent M20 slices while leaving maintainer/browser/provider acceptance for the later consolidated review requested by the maintainer.
4. Preserve the 30-second automatic GeoNames request floor and provider-directed quota/transport backoff; do not introduce broad retries.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0075-geonames-timing-settings.md`
- `Start-PhotoIdentity.ps1`
- `PhotoIdentity.launcher.example.json`
- `packaging/windows/PhotoIdentity.launcher.example.json`
- `packaging/windows/README.txt`
- `src/PhotoIdentity.Api/PhotoPlaceEnrichmentEndpoints.cs`
- `src/PhotoIdentity.Api/PhotoPlaceEnrichmentHostedService.cs`
- `src/PhotoIdentity.Web/PlaceEnrichmentContracts.cs`
- `tests/PhotoIdentity.Integration.Tests/PhotoPlaceEnrichmentEndpointTests.cs`
- `verify-launcher.ps1`
- `verify-package.ps1`
- `docs/operations/windows-package.md`
- `AGENTS.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
