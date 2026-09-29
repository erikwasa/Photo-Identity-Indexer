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

- [ ] A nonempty duplicate-only page terminates or advances through a bounded recovery path without repeated requests at an unchanged offset.
- [ ] Overlapping pages add each face once, retain deterministic order and reach later unique results without an unbounded loop.
- [ ] Bulk Assign and Accept suggestions show success as soon as commit succeeds; a delayed refill does not delay success or retain the action-busy state.
- [ ] A refill failure after commit preserves truthful success and reports refresh failure separately, without duplicate review actions.
- [ ] Changing filters or leaving the queue during refill cannot apply stale results to a new view; concurrent refill/load-more requests remain controlled.
- [ ] Normal initial loading, infinite scroll, near-end queues, single-face actions and bulk refill preserve selection, counts and audit/undo behavior.
- [ ] On the maintained Windows catalogue, selecting 10 faces and using each bulk path produces prompt feedback with no repeated-page request storm; record commit and refill timings separately.

## Verification requirements

Automated verification is required at the lowest practical layer: deterministic page fixtures for duplicate-only, overlapping and empty responses, plus slow/failed refill after successful commit and stale-response cancellation. Add persistence/API tests only where a reproduced ordering or count defect requires them; avoid unrelated host-heavy coverage.

Human verification is required on the maintained Windows catalogue for both bulk actions, with representative queue ordering and background regeneration where applicable. Record bounded request counts and separate commit/refill duration, using aggregate diagnostics without private identifiers.

Run relevant builds/tests and PhotoIdentity.Docs validate plus generate --check. This tracking change does not implement the fix.

## Completion notes

- Status: ready; no prerequisites beyond the existing review workflow.
- Investigation evidence: private maintainer report and aggregate stdout analysis, plus the missing no-progress guard in ReviewWorkspace.razor.
- Deferred: implementation, deterministic regression tests and maintained-catalogue acceptance.
