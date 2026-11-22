---
id: M20
title: Operator polish and archive throughput
status_source: ../status/milestones.yaml
depends_on: [M18, M19]
---

# M20: Operator polish and archive throughput

## Outcome

Photo Identity is easier to operate during large archive runs and less visually noisy during normal review. The milestone combines operator polish from the M19 maintainer pass with measured archive-throughput work.

## Work items

- [WI-0073](../work-items/WI-0073-ui-navigation-polish.md) — responsive UI/navigation polish and clearer archive state.
- [WI-0074](../work-items/WI-0074-face-review-suggested-person-filter.md) — filter Face Review by the current top suggested person.
- [WI-0075](../work-items/WI-0075-geonames-timing-settings.md) — operator-configurable GeoNames background timing.
- [WI-0076](../work-items/WI-0076-archive-throughput.md) — measure and materially improve archive-processing throughput.
- [WI-0077](../work-items/WI-0077-photo-viewer-simplification.md) — simplify Photo Details and location editing.
- [WI-0078](../work-items/WI-0078-versioned-metadata-refresh.md) — version metadata extraction and refresh stale rows.

## Maintainer verification

The interactive/operator acceptance for WI-0073, WI-0074, WI-0075, WI-0077 and WI-0078 passed on 2026-08-26. The detailed checklist remains in [M20-maintainer-verification-2026-08-26.md](M20-maintainer-verification-2026-08-26.md).

WI-0076 was measured separately. Diagnostics identified per-image detector/embedder initialization as the dominant local cost. PRs #210 and #211 established measurement and fixed the online-only derivative lifecycle; PR #212 safely reused the analysis session. On the same 155-image local corpus, throughput improved from about 359 images/hour to about 1,925 images/hour, with one model initialization and 154 reuses instead of 155 initializations. The online-only scenario also completed with balanced bounded hydration/release.

Lower-value duplicate-hash, prefetch and loop-delay ideas remain possible future optimizations but are not required to close the measured M20 bottleneck. M24/WI-0106 later demonstrated sustained production catch-up and incremental operation on the real PostgreSQL catalogue.

## Exit criteria

- [x] Known card/menu/hidden/archive-state and archive-return navigation issues are fixed without regressing previously verified M19 behavior.
- [x] Face Review can filter by current top suggested person and preserves that queue scope through Face Details navigation.
- [x] GeoNames automatic timing can be supplied through `PhotoIdentity.launcher.json`, lower supported overrides are honored, and effective pacing is operator-visible.
- [x] Archive throughput has a measured stage breakdown and a documented before/after improvement on representative hardware/data without weakening safety contracts.
- [x] Photo Details keeps secondary photographic metadata inside collapsed `All metadata` and presents Location in a read-first mode with an explicit Edit action.
- [x] Existing metadata rows carry an extraction-contract version and stale rows can be safely reprocessed to obtain fields added by newer readers.

M20 is complete.
