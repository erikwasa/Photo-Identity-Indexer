---
id: M20
title: Operator polish and archive throughput
status_source: ../status/milestones.yaml
depends_on: [M18, M19]
---

# M20: Operator polish and archive throughput

## Outcome

Photo Identity is easier to operate during large archive runs and less visually noisy during normal review. The milestone captures the concrete follow-up feedback from the 2026-08-19 M19 maintainer pass plus the first successful archive-metadata run after WI-0072.

The milestone focuses on six areas:

- responsive UI/navigation polish and clearer archive state;
- filtering Face Review by the current top suggested person;
- operator-configurable GeoNames background timing through the launcher settings file;
- materially improving archive-processing throughput without weakening bounded hydration, immutable revision verification or resumability;
- simplifying Photo Details so common information is prominent while editing controls and secondary metadata stay collapsed until needed;
- versioning metadata extraction so existing catalogue rows can be reprocessed when supported fields expand.

## Work items

- [WI-0073](../work-items/WI-0073-ui-navigation-polish.md) — fix card containment, hidden-person presentation/order, dismissible menus, favorite select type-ahead, archive return context and archive progress wording.
- [WI-0074](../work-items/WI-0074-face-review-suggested-person-filter.md) — filter the Face Review queue by the current rank-one suggested canonical person while preserving existing queue semantics/navigation.
- [WI-0075](../work-items/WI-0075-geonames-timing-settings.md) — make automatic GeoNames timing settings accepted and documented in the launcher settings file, with the current safe pacing default retained unless explicitly overridden by supported policy.
- [WI-0076](../work-items/WI-0076-archive-throughput.md) — profile and improve archive processing throughput, prioritizing model-session reuse, repeated full-file verification reads, batching and bounded OneDrive prefetch opportunities.
- [WI-0077](../work-items/WI-0077-photo-viewer-simplification.md) — reduce visible Photo Details metadata and make Location read-first/edit-on-demand.
- [WI-0078](../work-items/WI-0078-versioned-metadata-refresh.md) — version the metadata extraction contract and make existing rows eligible for bounded re-inspection when the supported metadata set changes.

## Existing-image metadata backfill

WI-0072 deliberately retains the explicit `POST /api/photo-metadata/backfill` operation for catalogue revisions that predate automatic archive metadata inspection. Backfill reads only originals that are already local, verifies them against the immutable revision and never requests OneDrive hydration merely for metadata.

The current selector, however, only chooses revisions with **no** `photo_capture_metadata` row. That means a photo inspected under an older reader contract is currently considered complete even if newer fields were added later. WI-0078 closes that gap with a durable extraction-contract version so default backfill can select both missing and stale rows, while retaining an explicit force/repair mode for current-version rows.

Until WI-0078 is implemented, existing rows that already have a capture-metadata inspection marker will **not** automatically be reprocessed just because the application now supports additional metadata fields.

## Archive-performance baseline

The maintainer observed roughly **100 images/hour** during the first successful archive-metadata run. Repository inspection shows several plausible sources of avoidable overhead that WI-0076 must measure before changing concurrency:

- bounded archive advancement deliberately advances at most one governed step at a time;
- active analysis is resumed with `maxAttemptsPerInvocation: 1`;
- every `ArchiveAnalysisCoordinator.StartAsync`/`ResumeAsync` invocation creates and disposes a `LocalInspectionJobHandler`, which constructs detector and embedder model objects;
- exact-original status/open operations may SHA-256 the same local file multiple times across metadata inspection, analysis, proxy generation and release checks;
- hydration admission already has a bounded concurrency policy, but the advancement loop generally prepares one pending revision at a time;
- the 500 ms active-loop delay contributes latency but is unlikely to explain the observed throughput by itself.

The optimization work should therefore start with per-stage timing and model/session/hash-read counts. Prefer eliminating repeated setup/I/O and processing safe batches before introducing broad parallel inference or database concurrency.

## Verification strategy

Each work item has focused automated coverage plus a maintainer browser/operator check. Archive throughput changes require before/after measurements on the same representative media set and must preserve:

- immutable revision/hash safety;
- bounded OneDrive hydration byte/concurrency limits;
- restart/resume and idempotency;
- correct metadata, face analysis, derivatives and review proxies;
- responsive UI while archive processing is active.

Metadata refresh verification must prove that a pre-WI-0072/legacy inspection row can gain newly supported fields without deleting manual Places or triggering metadata-only OneDrive hydration.

## Exit criteria

- Known card/menu/hidden/archive-state and archive-return navigation issues are fixed without regressing previously verified M19 behavior.
- Face Review can filter by current top suggested person and preserves that queue scope through Face Details navigation.
- GeoNames automatic timing can be supplied through `PhotoIdentity.launcher.json` and is documented with units/defaults/safety semantics.
- Archive throughput has a measured stage breakdown and a documented before/after improvement on representative hardware/data without weakening safety contracts.
- Photo Details keeps secondary photographic metadata inside collapsed `All metadata` and presents Location in a read-first mode with an explicit Edit action.
- Existing metadata rows carry an extraction-contract version and stale rows can be safely reprocessed to obtain fields added by newer readers.
