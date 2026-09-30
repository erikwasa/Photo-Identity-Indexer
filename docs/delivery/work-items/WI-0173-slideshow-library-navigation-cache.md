---
id: WI-0173
title: Reuse slideshow library cards and covers across navigation
milestone: M32
status_source: ../status/work-items.yaml
depends_on: [WI-0108]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0173: Reuse slideshow library cards and covers across navigation

## Objective and maintainer evidence

Preserve usable library cards and covers when returning from playback; refresh definitions and stale cover/count metadata in the background. Bound shared cover requests and memory; never cache Prepared authorization or immutable playback snapshots.

Reported 2026-09-30 (Europe/Stockholm) during M32 acceptance.

## Acceptance criteria and verification

1. Open Slideshows, play and exit, then navigate away and back. Cards/covers should remain visible while definitions refresh.
2. Edit/delete a collection in the full app; returning should refresh the library and its exact current count.
3. A cover/count failure must not prevent playback; stale membership must never authorize prepared playback.

## Status

Maintained desktop/phone verification remains outstanding.

## Implementation

A scoped WASM session service preserves ordinary/manual/Creative card definitions across navigation, immediately renders previous cards, then replaces them with fresh endpoint responses. Cards use stable identity keys. Smart and Creative cards sharing an anchor coalesce their cover/count request; a one-minute metadata cache avoids repeated queries on quick returns, retains display metadata during stale refresh/failure, caps stored covers at 256, and gates active cover requests to four. Smart definition save/delete invalidates its cached cover; version checks prevent older in-flight queries from repopulating it. Cancelling one card only cancels that consumer's wait. Manual covers use the existing lightweight thumbnail endpoint. Reload starts a fresh session; preparation receipts still use their independent exact-membership and local-file validation. Counts can reflect the last minute's display metadata; this cache never authorizes playback/preparation.

Focused non-host tests verify request coalescing, consumer cancellation isolation, warm reuse, stale display during refresh, and retry after failure. No new CI gate or host-heavy test is introduced. Maintained desktop/phone acceptance remains pending.

## Follow-up validation

The affected API/Web/test projects build, and 50 focused non-host slideshow/receipt/cache/route/Creative optimization tests pass. Documentation `validate` and `generate --check` pass. PostgreSQL-backed preparation tests could not run locally because the test admin connection was unavailable; those cases remain required in CI. Maintained archive/phone acceptance is not claimed.
