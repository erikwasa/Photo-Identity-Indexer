---
id: WI-0160
title: Replace Smart Collection paging with infinite scroll
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0159]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0160: Replace Smart Collection paging with infinite scroll

## Objective

Replace the visible previous/next paging controls in Smart Collection results with incremental infinite scrolling while keeping bounded server-side queries and deterministic result ordering.

## Why

Paging interrupts visual browsing of family-photo collections and makes the result grid feel like an administrative table. The server already supports bounded offset/limit queries, so the UI can progressively append result pages without loading the whole collection at once.

## In scope

- Remove ordinary result paging controls from the Smart Collections UI.
- Load the first bounded result batch normally and request additional batches as the operator approaches the end of the loaded grid.
- Append results without clearing already loaded photos.
- Keep a clear loading state and stop requesting once the reported total has been loaded.
- Preserve deterministic ordering and avoid duplicate cards if a load is retried.
- Preserve/restore useful scroll position when returning from Photo Details, including the collection navigation introduced by WI-0159.
- Keep API queries bounded; infinite scroll must not silently become an unbounded `QueryAll` browser request.
- Work on phone and desktop without requiring a manual "load more" action during normal browsing.

## Out of scope

- Virtualizing thumbnails that are already outside the viewport unless measurement shows it is needed.
- Changing Smart Collection sort order or filter semantics.
- Infinite scrolling other review surfaces.

## Acceptance criteria

- [x] Smart Collection results no longer show previous/next page controls.
- [x] Scrolling near the bottom loads the next bounded batch and appends it to the grid.
- [x] No result is duplicated or skipped while traversing a stable collection.
- [x] Loading stops when all results have been fetched.
- [x] Network/API calls remain bounded by the configured batch size rather than fetching the complete collection at once.
- [x] Returning from Photo Details can restore the previously browsed area without forcing the operator back to the first batch.
- [x] Automated coverage protects append behavior, total/boundary handling, retries and stable ordering.

## Verification requirements

Maintainer verification with a Smart Collection substantially larger than one batch on desktop and phone. Scroll through several batches, open/return from a photo, and confirm the browsing position and already-loaded results remain useful.

## Maintainer review — 2026-09-25

The maintainer confirmed WI-0160 works as expected after PR #422. Infinite scrolling replaces visible paging, additional bounded batches append during browsing, and the Smart Collection browsing context remains useful when returning from Photo Details.

## Completion notes

- PR #422 implemented WI-0160 together with WI-0161 because both affect the Smart Collection result browsing surface.
- The browser keeps the existing bounded 40-photo query model and appends successive batches rather than switching to an unbounded query.
- Append logic rejects gaps/ordering changes and treats retry overlap idempotently to avoid duplicate cards.
- Loading, retry and end-of-results states are explicit, and the return context introduced by WI-0159 is preserved across deeper scrolling.
- Automated coverage exercises bounded append, retry overlap, ordering/gap protection and transient continuation behavior.
- PR #422 workflow run #2164 completed successfully. Maintainer acceptance on 2026-09-25 completes WI-0160.
