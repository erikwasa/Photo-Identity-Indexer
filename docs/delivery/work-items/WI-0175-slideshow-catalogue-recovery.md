---
id: WI-0175
title: Fix slideshow catalogue connection churn and reload recovery
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0175: Fix slideshow catalogue connection churn and reload recovery

## Objective

Restore reliable slideshow verification after the 2026-10-01 maintained Windows/phone acceptance failure, tracked in GitHub issue #476.

## Maintainer evidence

Creative saved-recipe snapshots timed out after approximately 100 seconds. Three requests were cancelled in `catalogue-query`, before visual hashing; another returned HTTP 500 after approximately 98 seconds. Manual collection listing and prepared-original revalidation returned HTTP 500 with Npgsql connection failure and Windows socket error 10048. Stack traces used `Npgsql.UnpooledDataSource`; OS-wide ephemeral-port exhaustion is consistent with the evidence but has not been measured.

Already-local Smart/manual preparation displayed Prepared. Returning through navigation preserved cards, but F5 removed the Prepared indicator. A generic review application error was also reported; the server log does not establish its precise frontend exception. No private log, source path, collection/revision identifier or connection string is committed.

Code inspection found per-revision exclusion connection opens in Smart catalogue queries and manual collection responses, missing materialization deadlines in saved Creative routes, and receipt removal when collection loading fails. Existing production batch exclusion lookup can remove the N+1 work without weakening privacy. Receipts must remain hints: only current membership and local-byte validation can establish Prepared.

## Scope

- Batch Smart/manual exclusion checks while preserving ordering and privacy.
- Keep the API runtime catalogue pooled and bounded, preserving other connection settings and direct CLI/test database behavior.
- Apply the existing 60-second deadline to saved Creative preview/snapshot paths; propagate caller cancellation.
- Preserve receipts on temporary refresh/verification failure; expose retry without claiming Prepared before verification succeeds.
- Keep refresh/disposal and browser-storage errors inside recoverable component handling.

## Acceptance criteria

- [ ] Smart all-photo queries/snapshots and manual listing/snapshots perform batch exclusion checks, preserve ordering and never return excluded revisions.
- [ ] Runtime composition enables pooling even for a legacy `Pooling=false` connection string, retaining configured pool capacity; direct database construction remains unchanged.
- [ ] Every saved Creative preview/snapshot route observes the shared deadline, returns actionable HTTP 503 on deadline, and propagates caller cancellation.
- [ ] Failed collection refresh never deletes receipts based on incomplete membership; failed revalidation hides Prepared, preserves receipts and exposes retry.
- [ ] Retry revalidates saved receipts, confirms current membership/local bytes, and removes genuinely invalid receipts.
- [ ] Focused regression tests, affected builds and documentation checks pass.
- [ ] Maintained Windows/phone retest completes Creative playback, manual refresh and F5 Prepared verification without the reported errors.

## Verification requirements

Use direct repository/endpoint tests and component fixtures rather than new host-heavy tests. The generic frontend banner remains an unconfirmed symptom until retested; do not claim its precise cause from server evidence. Maintainer acceptance remains required for WI-0165, WI-0169 and WI-0173.

1. Package the PR after CI passes; restart the application.
2. Start saved Creative collections with targets 30/50, then the representative Broad/150 case twice in the same process. Record catalogue/total timings and completion/error outcomes.
3. Prepare already-local Smart/manual originals; exit, navigate back and F5 in the same browser/origin. Prepared must return after successful verification.
4. Simulate a temporary manual-list/revalidation HTTP failure in component tests: receipts survive and Prepared is hidden. Retry after recovery must restore Prepared without a full reload.
5. Edit membership or make a required original unavailable; successful verification must invalidate Prepared.
6. Verify desktop/phone navigation and monitor for HTTP 500/socket 10048 and generic application error. Capture browser console details if the banner recurs.

## Implementation and verification evidence

- Smart all-photo queries/pages/snapshots and manual collection listing/snapshots now use batch exclusion lookups, preserving order and source-copy privacy.
- API composition normalizes runtime pooling to true while preserving configured pool capacity and other connection settings; direct CLI/test construction is unchanged.
- Saved and ad hoc Creative preview/snapshot routes share the 60-second deadline and actionable HTTP 503. Caller cancellation propagates.
- Failed collection refresh preserves receipts; failed verification hides Prepared and exposes retry. Successful membership/local-byte verification still invalidates stale receipts. Generation and lifetime guards reject old responses; null browser entries and storage interop failures remain recoverable.
- Added focused non-host batch/deadline/component fixtures, including receipt preservation/retry, membership/local-byte invalidation, stale validation and navigation during verification. No required CI gate or database migration changed.
- [Windows preparation CI](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/36928766356) built the affected dependency graph and passed **23/23** focused tests (0 skipped, reported test duration **576 ms**). `PhotoIdentity.Docs generate`, `validate`, `review WI-0175`, and `generate --check` passed. The temporary branch-only preparation workflow removed itself after validation; it does not remain in the final PR.
- Local whitespace and C# syntax checks passed. Local .NET 10 startup was unavailable (CoreCLR HRESULT 0x8007000E); executable evidence above is from Windows CI.
- Standard PR CI and maintained Windows/phone acceptance remain required. WI-0175 is in_review, not completed. The exact exception behind the generic frontend banner remains unconfirmed until retested.
