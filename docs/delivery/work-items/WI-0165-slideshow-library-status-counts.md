---
id: WI-0165
title: Show slideshow preparation state and photo counts in the library
milestone: M32
status_source: ../status/work-items.yaml
depends_on: [WI-0108, WI-0146, WI-0158]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0165: Show slideshow preparation state and photo counts in the library

## Objective

Make the Slideshows gallery easier to scan by showing a small preparation indicator where preparation is known to match the current slideshow contents and a compact, truthful photo-count indicator for each slideshow entry.

The change must preserve the responsive slideshow-library behavior established by WI-0108. Card rendering must not become dependent on expensive snapshot generation, catalogue-wide work or an avoidable N+1 blocking request sequence.

## User-facing semantics

- Use a small **Prepared** indicator rather than saying **Ready to play**. An unprepared slideshow is still playable; preparation only means the relevant originals have already been made locally ready according to the existing preparation workflow.
- Show no negative/unready badge when a slideshow has no valid preparation receipt. Absence of the indicator must not imply that playback is unavailable.
- Preparing, parent-attention and startup states keep precedence over the passive Prepared indicator.
- Photo counts should be compact, for example `53 photos`, and should not dominate the cover/title/play affordance.

## Scope

- Add an unobtrusive Prepared badge/dot/check treatment to ordinary slideshow-library cards when existing preparation state or a persisted receipt is verified against the exact current revision set.
- Preserve preparation receipt invalidation semantics: when a Smart/manual slideshow's current contents no longer match the prepared revision set, the Prepared indicator must disappear rather than become stale.
- Restore a valid Prepared indicator after browser reload using the existing preparation-receipt mechanism without blocking initial card rendering on new expensive work.
- Show the exact current member count for manual slideshow collections using their persisted revision membership.
- Show the current matching-photo count for saved Smart Collection slideshow entries.
- Obtain Smart Collection counts through a bounded/count-oriented path or asynchronously after the initial library cards render. Do not create full slideshow snapshots solely to obtain a number, and do not regress the WI-0108 library-load latency boundary.
- Show a truthful count/quantity treatment for Creative Collections. If the recipe only guarantees a target maximum rather than an exact materialized selection, display that distinction (for example `up to 50 photos`) rather than presenting the recipe target as an exact count.
- If an exact Creative Collection selection/count is already materialized by an existing bounded contract, it may be shown as exact; do not introduce expensive selection generation merely to decorate the library card.
- Counts that are still loading or temporarily unavailable should fail softly and must not prevent the slideshow card from being playable.
- Keep the indicators legible on desktop and phone layouts and expose equivalent accessible text/labels rather than relying on colour alone.
- Cover Smart, manual and Creative Collection cards consistently while respecting their different persistence/selection semantics.

## Performance boundary

WI-0108 intentionally made `/api/slideshows/collections` cheap by returning saved definitions without catalogue query work. This work item must preserve that boundary. The implementation may add a dedicated count-only/batched API or defer count hydration until after the initial card render, but it must not make the initial slideshow-library response generate full collection snapshots or perform unnecessary image/file preparation.

## Acceptance criteria

- [ ] A slideshow with preparation verified for its exact current revision set displays a small Prepared indicator.
- [ ] Unprepared slideshows remain visibly playable and are not labelled unavailable or not-ready.
- [ ] Preparing, parent-attention and starting states remain clear and take precedence over the passive Prepared treatment.
- [ ] A persisted valid preparation receipt restores the Prepared indicator after reload.
- [ ] Changing slideshow membership/filter results invalidates a stale Prepared indicator once the current revision set no longer matches the receipt.
- [ ] Manual slideshow cards show their exact persisted photo count.
- [ ] Smart Collection cards show a count that agrees with the current collection query/snapshot total at verification time.
- [ ] Creative Collection cards never present `TargetCount` as an exact member count when the actual generated selection may contain fewer photos; the UI distinguishes target/maximum from an exact materialized count.
- [ ] Count retrieval failure does not prevent cards from rendering or starting playback.
- [ ] Initial slideshow-library rendering remains independent of full slideshow snapshot creation and preserves the bounded library-load behavior established by WI-0108.
- [ ] The UI remains compact and readable on the maintained desktop Edge and phone layouts.
- [ ] Prepared/count information is accessible without relying on colour alone.
- [ ] Automated tests cover preparation-indicator visibility/invalidation, manual exact counts, Smart count semantics and Creative target-versus-exact wording.

## Verification plan

1. Load `/slideshows` with representative Smart, manual and Creative Collection entries and confirm cards become usable before any deferred count hydration completes.
2. Verify a manual slideshow count against its persisted revision membership.
3. Verify a Smart Collection count against a newly created slideshow snapshot/query total without requiring the card itself to create that snapshot.
4. Verify Creative Collection wording against a recipe whose target can exceed the available/generated selection.
5. Prepare one Smart/manual slideshow and confirm the compact Prepared indicator appears.
6. Reload the page and confirm a still-valid preparation receipt restores the indicator.
7. Change the relevant collection membership/filter, reload/refresh its state and confirm the stale indicator is removed.
8. Exercise preparing and parent-attention states and confirm they remain more prominent than the passive Prepared indicator.
9. Verify desktop Edge and phone layouts for readability, tap targets and accessible text.
10. Run the existing slideshow-library performance diagnostics to ensure the change does not reintroduce blocking library or snapshot latency.
