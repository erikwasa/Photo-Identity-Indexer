---
id: WI-0170
title: Bound review queue refill and show bulk face decisions promptly
milestone: M34
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres]
---

# WI-0170: Bound review queue refill and show bulk face decisions promptly

## Objective

Make bulk face review report committed decisions promptly and prevent review queue refill from repeatedly fetching pages without progress.

## Why

Maintainer report on 2026-09-30: selecting 10 faces and initiating a bulk action appeared to do nothing for several minutes before displaying an accepted-suggestions success message.

Aggregate analysis of the privately supplied stdout excerpt found 271 completed suggestion-gallery GET requests: 79 at offset 122, 191 at offset 123, and one at offset 125. They all returned HTTP 200, taking approximately 82.7 seconds in aggregate. The excerpt contains no bulk preview/commit request, so it does not establish persistence latency or the precise ordering relative to the click. Do not commit the raw log, names, face identifiers or private catalogue data.

Code inspection of ReviewWorkspace.razor confirms that LoadFacesAsync deduplicates returned face IDs, uses Faces.Count as the next offset, and continues until the target count/total is reached or the server returns an empty page. A nonempty page adding zero unique faces has no termination condition and can repeatedly request the same offset. Bulk review awaits BackfillAfterBulkAsync after setting the success message, coupling event completion to refill.

The reported success wording belongs to the Accept suggestions path; the separate Assign path has different wording. Both paths share refill behavior and require verification. The source of overlapping/repeated server pages remains an investigation target; the log does not prove duplicate database rows, CLIP contention, or slow assignment writes.

## In scope

- Reproduce nonempty duplicate-only and overlapping page responses using deterministic fixtures.
- Bound queue loading when a response adds no unique faces; do not retry the same page indefinitely.
- Investigate suggestion-gallery ordering, count/membership consistency and paging under concurrent review/regeneration; correct any reproduced defect within this boundary.
- Track server paging progress separately from displayed unique count where needed; preserve queue filtering and exclusion rules.
- Show committed review success and clear action-busy state promptly, independently of slow refill.
- Expose a loading/refill state and distinguish a refill failure from a failed commit; never encourage resubmitting an already committed decision.
- Keep refill bounded and cancellation/navigation safe, preventing stale responses or concurrent loaders from corrupting the current queue.
- Preserve canonical audit history, preview revalidation, undo and existing single-face actions.

## Out of scope

- CLIP/caption scheduling changes, recognition-model changes and broad database tuning.
- Reworking the entire review UI or changing automatic-assignment policy.
- Claiming the private delay is fully resolved without maintained-catalogue verification.

## Acceptance criteria

- [x] A nonempty duplicate-only page terminates or advances through a bounded recovery path without repeated requests at an unchanged offset.
- [x] Overlapping pages add each face once, retain deterministic order and reach later unique results without an unbounded loop.
- [x] Bulk Assign and Accept suggestions show success as soon as commit succeeds; a delayed refill does not delay success or retain the action-busy state.
- [x] A refill failure after commit preserves truthful success and reports refresh failure separately, without duplicate review actions.
- [x] Changing filters or leaving the queue during refill cannot apply stale results to a new view; concurrent refill/load-more requests remain controlled.
- [x] Normal initial loading, infinite scroll, near-end queues, single-face actions and bulk refill preserve selection, counts and audit/undo behavior.
- [x] On the maintained Windows catalogue, selecting 10 faces and using each bulk path produces prompt feedback with no repeated-page request storm; record commit and refill timings separately.

## Verification requirements

Automated verification is required at the lowest practical layer: deterministic page fixtures for duplicate-only, overlapping and empty responses, plus slow/failed refill after successful commit and stale-response cancellation. Add persistence/API tests only where a reproduced ordering or count defect requires them; avoid unrelated host-heavy coverage.

Human verification is required on the maintained Windows catalogue for both bulk actions, with representative queue ordering and background regeneration where applicable. Record bounded request counts and separate commit/refill duration, using aggregate diagnostics without private identifiers.

Run relevant builds/tests and PhotoIdentity.Docs validate plus generate --check.

## Implementation and verification evidence — 2026-09-30

- ReviewWorkspace now counts paging progress separately from displayed unique cards. Overlapping responses advance by returned row count, and removals decrement the cursor for the changed queue membership.
- A nonempty page adding zero faces, an empty page inconsistent with the total, or five partial/overlapping requests pauses loading with an explicit Reload queue action. Normal exhaustion does not show a warning. Pausing disables automatic scroll requests; it does not silently claim the queue is complete.
- Bulk Assign and Accept suggestions complete their commit event before refill finishes. Success and cleared selection render immediately; refill has a separate loading state and failures preserve committed success. Single-face decisions reuse the same bounded refill path.
- Loads use cancellation plus a generation guard; newer filters, decisions and component disposal invalidate older responses. Concurrent initial/load-more requests are suppressed. Bulk preview/commit and canonical audit/undo contracts are unchanged.
- Server investigation: identity_suggestion_rankings has a primary key on face/model/hash/rank, and gallery order ends with face ID. No duplicate-row or unstable tie-breaker defect was reproduced, so no persistence change or migration was introduced. Live regeneration/review can still shift an offset-based queue; this implementation bounds that behavior rather than claiming snapshot pagination.
- Added 11 deterministic component tests in ReviewWorkspacePagingTests. They exercise the actual component and event rendering using a small renderer and controlled HTTP responses, without an API host, PostgreSQL, production workers or browser. Coverage includes duplicate-only/overlapping/empty pages, the request budget, both bulk actions with delayed/failed refill, concurrent load suppression, reset and disposal.
- Web build passed with zero warnings/errors. Filtered component tests passed: 11/11, approximately 1.23 seconds total test-run time on this Linux workspace. The existing integration assembly and required CI gate are unchanged.
- Remaining acceptance: repository CI and maintained Windows/private-catalogue verification below. Do not mark completed from deterministic fixtures alone.

## Maintained Windows verification

1. Open the review queue with the suggestion ordering used for the reported delay. Select 10 faces and use Accept suggestions; separately verify Assign on an appropriate selection.
2. Confirm Saving feedback appears during the request, success appears when commit finishes, selected faces are reconciled, and refill is visibly separate. Inspect browser Network timings for preview, commit and refill separately.
3. Confirm stdout no longer contains hundreds of consecutive requests at the same offset. If the queue shifts during background regeneration, loading must stop with Reload queue after no progress or the bounded request budget.
4. Change filters during refill and navigate away/back. Verify older pages do not populate the new queue. Check normal scroll, near-end loading and single-face decisions/undo.
5. If refill fails after a successful commit, verify the success remains visible and Reload queue retries reading only; do not submit the same decision again to refresh the view.

## Completion notes

- Files changed: ReviewWorkspace.razor, ReviewWorkspacePagingTests.cs, canonical WI-0170 lifecycle/documentation and generated status views, BUILD_CONTEXT.md.
- Trade-off: offset paging under concurrent mutations remains a changing view. The component bounds recovery and offers explicit reload; a stable server snapshot/cursor contract is not introduced without a reproduced need.
- Deferred: maintained Windows/private-catalogue acceptance and any separately reproduced server ordering/count defect.
- Commands run: web build; filtered ReviewWorkspacePagingTests; PhotoIdentity.Docs show, review, generate, validate and generate --check.

## Maintainer acceptance — 2026-10-02 (Europe/Stockholm)

The maintainer reports the review queue seems to work fine and explicitly states that timing measurements are unnecessary. The maintainer accepts the observed behavior in place of the planned commit/refill timing report; no numerical timing or unreported scenario is claimed. Canonical lifecycle is completed, verified by erikwasa. This acceptance supersedes earlier outstanding-verification statements above.
