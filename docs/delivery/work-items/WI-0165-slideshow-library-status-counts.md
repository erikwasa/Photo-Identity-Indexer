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
- Treat successful best-quality preparation performed during normal slideshow startup as preparation of that exact immutable revision set, so returning to the slideshow library can establish the same reusable Prepared state as standalone **Prepare originals**.
- Revalidate a persisted/prepared snapshot before relying on it for later best-quality playback, while avoiding UI wording that implies a new download when the originals are already local and verification completes immediately.
- Show the exact current member count for manual slideshow collections using their persisted revision membership.
- Show the current matching-photo count for saved Smart Collection slideshow entries.
- Obtain Smart Collection counts through a bounded/count-oriented path or asynchronously after the initial library cards render. Do not create full slideshow snapshots solely to obtain a number, and do not regress the WI-0108 library-load latency boundary.
- Show a truthful count/quantity treatment for Creative Collections. If the recipe only guarantees a target maximum rather than an exact materialized selection, display that distinction (for example `up to 50 photos`) rather than presenting the recipe target as an exact count.
- If an exact Creative Collection selection/count is already materialized by an existing bounded contract, it may be shown as exact; do not introduce expensive selection generation merely to decorate the library card.
- Counts that are still loading or temporarily unavailable should fail softly and must not prevent the slideshow card from being playable.
- Keep the indicators legible on desktop and phone layouts and expose equivalent accessible text/labels rather than relying on colour alone.
- Cover Smart, manual and Creative Collection cards consistently while respecting their different persistence/selection semantics.

## Performance boundary

WI-0108 intentionally made `/api/slideshows/collections` cheap by returning saved definitions without catalogue query work. This work item must preserve that boundary. The implementation may add a dedicated count-only/batched API or defer count hydration until after the initial card render, but it must not make the initial slideshow-library response generate full slideshow snapshots or perform unnecessary image/file preparation.

## Implementation progress

PR #442 implements the library indicators without changing `/api/slideshows/collections`. Manual counts come from the already-loaded persisted revision membership. Smart counts reuse the existing deferred `limit=1` cover query and its `Total`, so no additional Smart count request or slideshow snapshot is introduced solely for the badge. Creative Collection cards display the recipe quantity as `Up to N photos` rather than an exact materialized count.

The ordinary Smart/manual cards show a compact text-and-check **Prepared** badge only after the existing preparation state has been verified as `ready`; starting, preparing and parent-attention states suppress that passive badge. Persisted manual receipts are now validated directly against the current manual revision membership before prepared-original revalidation, fixing the previous Smart-route-only reload behavior. Automated presentation/receipt tests cover exact/manual/Smart/Creative count semantics, Prepared precedence and revision-set invalidation.

### Maintainer follow-up — 2026-09-29

Real-phone verification found a remaining Prepared-state continuity gap, so WI-0165 must remain `in_review` rather than being completed. A slideshow whose originals had just been downloaded through normal best-quality playback could still appear in the library without a **Prepared** indication. Reopening that collection showed `0 / 14` briefly and then started about half a second later, consistent with fast revalidation of already-local originals rather than a new OneDrive hydration.

Standalone **Prepare originals** records a reusable preparation receipt when it completes, but successful preparation initiated by normal slideshow startup does not currently feed the same library receipt/state consistently. The follow-up must make successful player-triggered preparation establish the exact-snapshot Prepared state while preserving later revalidation and invalidation if the files are evicted or collection membership changes.

Maintainer desktop/phone verification and final CI evidence therefore remain pending.

## Acceptance criteria

- [ ] A slideshow with preparation verified for its exact current revision set displays a small Prepared indicator.
- [ ] Successful best-quality preparation initiated by normal slideshow playback records/reuses the exact-snapshot preparation state so the slideshow library can subsequently show **Prepared**.
- [ ] Reopening an already-local prepared slideshow may revalidate the originals, but the transient UI does not misleadingly imply that a new network download is required when verification completes immediately.
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
- [ ] Automated tests cover preparation-indicator visibility/invalidation, player-triggered preparation continuity, manual exact counts, Smart count semantics and Creative target-versus-exact wording.

## Verification plan

1. Load `/slideshows` with representative Smart, manual and Creative Collection entries and confirm cards become usable before any deferred count hydration completes.
2. Verify a manual slideshow count against its persisted revision membership.
3. Verify a Smart Collection count against a newly created slideshow snapshot/query total without requiring the card itself to create that snapshot.
4. Verify Creative Collection wording against a recipe whose target can exceed the available/generated selection.
5. Prepare one Smart/manual slideshow using standalone **Prepare originals** and confirm the compact Prepared indicator appears.
6. Start an unprepared slideshow with best-quality preparation enabled, allow playback preparation to finish, exit to the library and confirm the same collection now shows **Prepared**.
7. Reopen that already-local prepared slideshow and confirm any verification transition resolves quickly without presenting it as a fresh download; playback still verifies the exact snapshot before relying on originals.
8. Reload the page and confirm a still-valid preparation receipt restores the Prepared indicator.
9. Change the relevant collection membership/filter, reload/refresh its state and confirm the stale indicator is removed.
10. Exercise preparing and parent-attention states and confirm they remain more prominent than the passive Prepared indicator.
11. Verify desktop Edge and phone layouts for readability, tap targets and accessible text.
12. Run the existing slideshow-library performance diagnostics to ensure the change does not reintroduce blocking library or snapshot latency.

### Maintainer acceptance — 2026-09-30 (Europe/Stockholm)

Manual slideshow with 53 photos took 3 minutes 20 seconds to begin with **Prepare originals** enabled. After preparation and reload, the library still did not show **Prepared**. Acceptance failed; the item was reopened for implementation. The supplied stdout log records preparation polling and served originals but does not separate cloud download time from verification time, so the entire delay must not be attributed to either without measurement.

The follow-up records successful player preparation using the same browser-local receipt key and exact immutable revision set as standalone preparation. Library reload continues to check membership and original availability/content; a receipt never bypasses those checks. Original status checks and receipt revalidation now use four workers, while hydration admission/pinning remain governed by the existing capacity policy. This reduces serial verification overhead but cannot guarantee cloud-download duration. Focused receipt tests cover reload, replacement, unrelated receipts and corrupt browser state.

Retest the 53-photo case twice, noting whether originals are cloud-only or already local; check **Prepared** after exit/reload and its removal after membership change or eviction. Desktop/phone acceptance remains outstanding.

## Follow-up validation

The affected API/Web/test projects build, and 50 focused non-host slideshow/receipt/cache/route/Creative optimization tests pass. Documentation `validate` and `generate --check` pass. PostgreSQL-backed preparation tests could not run locally because the test admin connection was unavailable; those cases remain required in CI. Maintained archive/phone acceptance is not claimed.

## Maintainer acceptance — 2026-10-01 (Europe/Stockholm)

Acceptance failed and further verification stopped. Already-local Smart/manual preparation displays Prepared; navigation returns retain cards, but F5 loses the indicator. Manual listing and original revalidation return HTTP 500 with unpooled PostgreSQL connection failure / Windows socket 10048. Temporary list failure can incorrectly remove receipts; failed byte revalidation cannot establish Prepared. WI-0175 / issue #476 owns the repair and recovery tests. This item remains in review; no full acceptance is claimed.
