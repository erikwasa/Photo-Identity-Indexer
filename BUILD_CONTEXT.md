# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**M20 — Operator polish and archive throughput** remains the active delivery focus.

The maintainer completed a consolidated browser/operator review on **2026-08-21** covering merged WI-0073, WI-0074, WI-0075, WI-0077 and WI-0078. The authoritative review/implementation plan is `docs/delivery/milestones/M20-maintainer-review-2026-08-21.md`.

Review outcome:

- **WI-0074** passed maintainer verification; no corrective filtering-semantic work is requested.
- **WI-0078** passed real-catalogue maintainer verification; PR #195 exact-head workflow #1207 (`32307001482`) was also green. No corrective metadata-refresh work is requested.
- **WI-0073** needs a corrective UI/state slice: collapsed suggested-person picker, denser Queue controls, contained Smart Collection people rows, simplified Maintain People cards with real distinct-photo counts, and an archive advancement state-model fix so `Waiting for OneDrive` is only used when OneDrive is the sole blocker.
- **WI-0075** needs a pacing-policy correction: 30000 ms remains the default automatic GeoNames interval but is not a hard minimum; explicit lower non-negative values must be honored and effective pacing must not be silently overridden by a second throttle.
- **WI-0077** needs a presentation correction: compact metadata grid, normal-width GPS cell, Location immediately after GPS/capture metadata, and a compact city + most-specific-locality read label while preserving the full canonical hierarchy.
- **WI-0064/WI-0065** need a GeoNames language-policy correction: Sweden uses local-language names, non-Swedish results use English, with cache/provider-contract semantics that avoid repeatedly paying for duplicate foreign-coordinate lookups.

This branch/PR is intentionally **documentation only**. Do not start corrective product-code implementation until the maintainer approves/merges the documentation PR.

WI-0076 remains separate archive-throughput work. Do not mix the review corrections above into WI-0076.

## Next concrete step

1. Review the docs-only M20 maintainer-review planning PR.
2. If the maintainer requests changes, update the documented correction contracts only; do not implement product code yet.
3. After the maintainer approves/merges the planning PR, implement corrective slices under their owning work items/domains (WI-0073, WI-0075, WI-0077 and WI-0064/WI-0065).
4. Return the changed interactive/operator behavior for focused maintainer verification before reconciling final lifecycle completion.
5. Keep WI-0076 isolated from these corrective slices.

## Relevant files

- `docs/delivery/milestones/M20-maintainer-review-2026-08-21.md`
- `docs/delivery/work-items/WI-0073-ui-navigation-polish.md`
- `docs/delivery/work-items/WI-0074-face-review-suggested-person-filter.md`
- `docs/delivery/work-items/WI-0075-geonames-timing-settings.md`
- `docs/delivery/work-items/WI-0077-photo-viewer-simplification.md`
- `docs/delivery/work-items/WI-0078-versioned-metadata-refresh.md`
- `docs/delivery/work-items/WI-0065-automatic-place-enrichment.md`
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
