---
id: WI-0054
title: Polish archive viewing, progress and availability
milestone: M12
status_source: ../status/work-items.yaml
depends_on: [WI-0041, WI-0042]
related_adrs: [ADR-0007]
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0054: Polish archive viewing, progress and availability

## Objective

Address usability inconsistencies discovered during real Windows/OneDrive verification of the bounded permanent-archive workflow, without changing the accepted storage, hydration or managed-release policy.

## Context

The maintainer verified WI-0042 and WI-0041 on the real archive on 2026-08-10 and then reported three minor follow-ups:

1. Archive `View` on a `Local + Verified + Pending` revision shows no image because the strict viewer requires a durable review proxy, while proxy creation currently follows analysis.
2. Archive `Progress` shows only the latest processing run (for example `71 / 71`) and is easily mistaken for cumulative archive analysis, which can already be higher (for example `126`).
3. Explicit original hydration is observed live by `/original/status`, but Archive can continue showing the previous persisted `online-only` availability until another archive operation records the transition.

After PR #118 was merged, a 20-image mixed local/online-only acceptance run exposed one more progress issue: the displayed latest analysis run repeatedly changed IDs and reset from `1 / 8` to `0 / 8` while first-time online-only sources were being verified. The analysis results remained durable, but first-time verification of unrelated assets was cancelling active run snapshots more broadly than necessary, and the UI was exposing those internal batch replacements as operator progress.

## In scope

- Keep normal viewing free of implicit OneDrive hydration.
- If a durable review proxy exists, continue to use it as the preferred viewer source.
- If no proxy exists but the exact original is already local and revision-verified, allow the viewer to render a normal review-sized preview from those already-local bytes without changing pin/hydration state.
- If neither a proxy nor an already-local verified original is available, show a clear viewer message rather than a broken image.
- Distinguish internal analysis batches from operator-facing Archive advancement progress.
- Persist the live availability observed by explicit original status/hydrate/release operations so Archive reflects those transitions without requiring `Advance archive`.
- Preserve the accepted behavior that Photo-Identity-owned hydration remains eligible for managed release when later archive work no longer needs it.
- Preserve stale-revision cancellation when reverification actually changes an asset whose previous revision is still queued or running.

## Out of scope

- Parallelizing unattended archive advancement.
- Keeping explicit viewer hydration local indefinitely.
- Changing the free-space reserve, managed byte budget, concurrency, LRU or ownership rules accepted under WI-0042.
- Generating review proxies during lightweight sync.
- Replacing the immutable SHA-256 revision identity model.
- Inventing a single percentage for verification, analysis, proxy generation and release when those stages do not share one meaningful denominator.

## Acceptance criteria

- [x] `Local + Verified + Pending` archive revisions can be viewed at normal review size even when their durable proxy has not yet been generated.
- [x] Viewing an online-only revision with no proxy never hydrates the original as a GET side effect.
- [x] A missing preview is represented by an explanatory UI state rather than a broken image.
- [x] The Archive page distinguishes internal analysis-batch progress from cumulative archive analysis.
- [x] Calling explicit original status/hydrate/release updates the persisted Archive availability observation to the live OneDrive state.
- [x] A managed explicit hydration may still be released by later archive advancement under the existing WI-0042 ownership policy.
- [x] Regression tests cover local viewer fallback, no implicit online-only hydration and availability reconciliation.
- [x] Full build/test/docs verification remains green.
- [x] First-time verification of an unrelated asset does not cancel a nonterminal analysis batch; cancellation is reserved for a stale queued/running revision of the same asset.
- [x] Active Archive advancement shows stable archive-stage counters rather than the replaceable internal analysis-batch ID/progress.

## Implementation notes

- Added `/api/collections/photos/{revisionId}/viewer-preview`: durable proxy first; otherwise a transient review-sized JPEG may be rendered only from `CollectionOriginalAccessService.OpenVerifiedAsync`, which never hydrates and only opens already-local exact-revision bytes.
- The viewer exposes an explanatory no-preview state when neither a proxy nor an already-local verified original is available.
- Explicit original access persists every observed OneDrive availability state into the Archive availability table.
- While unattended advancement is active, Archive shows stable stage counters for analysis coverage, source verification, pending analysis and downloading. The replaceable processing-run ID is shown only afterward as diagnostic analysis-batch information.
- Source verification now reports both the previous verified revision and whether immutable identity actually changed. First-time verification has no previous revision and therefore cannot invalidate an existing batch.
- A nonterminal analysis batch is cancelled only when reverification changes identity and that exact previous revision is still queued or running in the batch. Durable completed analysis is not invalidated merely because another asset becomes verified.
- `NewRevision` remains a persistence fact; `RevisionChanged` is the semantic used for stale-work invalidation, so reselection of previously seen content is handled correctly.
- Managed-hydration release semantics remain unchanged.

## Completion notes

- Files changed across WI-0054: viewer/original-access API and web pages, Archive progress UI, source-verification and bounded-analysis orchestration, integration tests and operator/delivery docs.
- Trade-offs: transient fallback rendering performs an exact SHA-256 verification/read when no durable proxy exists; this is intentionally preferred to implicit hydration or serving unverified bytes. Active advancement uses several truthful stage counters instead of a fabricated all-pipeline percentage.
- Deferred work: parallel unattended archive advancement remains outside this work item. No WI-0054 acceptance blocker remains.
- Validation: GitHub Actions build run 802 (`31436697991`) passed Release build, the full automated test suite, living/generated documentation checks, hosted review smoke, Windows mixed-media verification and report assertions for the original WI-0054 product changes.
- Corrective validation: GitHub Actions build run 822 (`31441519751`) passed the same full repository gate on corrective head `7d511a9ac5234528139650fb8b2a06d0f0a34c7f`, including regression coverage for first-time verification and true revision identity change.
- Human verification: the maintainer completed post-merge WI-0054 verification on the real Windows/OneDrive archive on 2026-08-11 and accepted the viewer fallback, progress stability and availability behavior after the corrective follow-up.
- Commands run: repository CI plus `PhotoIdentity.Docs validate/generate --check` before review.
