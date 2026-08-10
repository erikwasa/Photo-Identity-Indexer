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

Address three usability inconsistencies discovered immediately after successful real Windows/OneDrive verification of WI-0042 and WI-0041, without changing the accepted bounded-storage or managed-release policy.

## Context

The maintainer verified WI-0042 and WI-0041 on the real archive on 2026-08-10 and then reported three minor follow-ups:

1. Archive `View` on a `Local + Verified + Pending` revision shows no image because the strict viewer requires a durable review proxy, while proxy creation currently follows analysis.
2. Archive `Progress` shows only the latest processing run (for example `71 / 71`) and is easily mistaken for cumulative archive analysis, which can already be higher (for example `126`).
3. Explicit original hydration is observed live by `/original/status`, but Archive can continue showing the previous persisted `online-only` availability until another archive operation records the transition.

## In scope

- Keep normal viewing free of implicit OneDrive hydration.
- If a durable review proxy exists, continue to use it as the preferred viewer source.
- If no proxy exists but the exact original is already local and revision-verified, allow the viewer to render a normal review-sized preview from those already-local bytes without changing pin/hydration state.
- If neither a proxy nor an already-local verified original is available, show a clear viewer message rather than a broken image.
- Clarify latest-run progress versus cumulative archive analysis in the Archive UI.
- Persist the live availability observed by explicit original status/hydrate/release operations so Archive reflects those transitions without requiring `Advance archive`.
- Preserve the accepted behavior that Photo-Identity-owned hydration remains eligible for managed release when later archive work no longer needs it.

## Out of scope

- Parallelizing unattended archive advancement.
- Keeping explicit viewer hydration local indefinitely.
- Changing the free-space reserve, managed byte budget, concurrency, LRU or ownership rules accepted under WI-0042.
- Generating review proxies during lightweight sync.
- Replacing the immutable SHA-256 revision identity model.

## Acceptance criteria

- [x] `Local + Verified + Pending` archive revisions can be viewed at normal review size even when their durable proxy has not yet been generated.
- [x] Viewing an online-only revision with no proxy never hydrates the original as a GET side effect.
- [x] A missing preview is represented by an explanatory UI state rather than a broken image.
- [x] The Archive page labels latest processing-run progress explicitly and separately reports cumulative archive analysis.
- [x] Calling explicit original status/hydrate/release updates the persisted Archive availability observation to the live OneDrive state.
- [x] A managed explicit hydration may still be released by later archive advancement under the existing WI-0042 ownership policy.
- [x] Regression tests cover local viewer fallback, no implicit online-only hydration and availability reconciliation.
- [x] Full build/test/docs verification remains green.

## Implementation notes

- Added `/api/collections/photos/{revisionId}/viewer-preview`: durable proxy first; otherwise a transient review-sized JPEG may be rendered only from `CollectionOriginalAccessService.OpenVerifiedAsync`, which never hydrates and only opens already-local exact-revision bytes.
- The viewer now exposes an explanatory no-preview state when neither a proxy nor an already-local verified original is available.
- Explicit original access now persists every observed OneDrive availability state into the Archive availability table.
- The Archive processing counter is relabelled `Latest run progress`; cumulative archive analysis remains the top-level `Analyzed` count.
- Managed-hydration release semantics remain unchanged.

## Completion notes

- Files changed: `src/PhotoIdentity.Api/CollectionOriginalAccessService.cs`, `src/PhotoIdentity.Api/CollectionViewerPreviewEndpoints.cs`, `src/PhotoIdentity.Api/Program.cs`, `src/PhotoIdentity.Web/Pages/Photo.razor`, `src/PhotoIdentity.Web/Pages/Archive.razor`, integration tests and operator docs.
- Trade-offs: transient fallback rendering performs an exact SHA-256 verification/read when no durable proxy exists; this is intentionally preferred to implicit hydration or serving unverified bytes.
- Deferred work: parallel unattended archive advancement remains outside this work item.
- Commands run: repository CI plus `PhotoIdentity.Docs validate/generate --check` before review.
