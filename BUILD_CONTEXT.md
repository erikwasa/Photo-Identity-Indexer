# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read the target shard and linked document before continuing.

## Current focus

WI-0165 / issue #499: PR #505 is merged. On 2026-10-04 (Europe/Stockholm), maintained re-verification passed: player-triggered preparation now produces **Prepared** on return to `/slideshows`, F5/reload restores it after revalidation, and the maintained behavior works as expected. Lifecycle closeout remains separate from the WI-0185 follow-up.

WI-0178 / issue #481: PR #501 is merged. On 2026-10-04 (Europe/Stockholm), maintained desktop/phone/PWA verification passed and the slideshow-library filtering behavior works as expected. Lifecycle closeout remains separate from the WI-0185 follow-up.

WI-0185 / issue #503: implementation PR #506 and acceptance-coverage PR #507 are merged. Maintained verification found the original `datetime-local` From/To controls unusable: typed times reset, calendar-selected values disappeared, and applying the range appeared to have no effect. The follow-up changes the UI to ordinary date-only From/To controls. From maps to local midnight inclusively; To includes the complete selected local day by mapping to the following local midnight as the API's exclusive upper bound. Keep WI-0185 `in_review` until the date filter interaction and remaining catalogue-scale/provenance/correction checks pass.

WI-0182 / issue #490: PR #509 implements the evaluation-only technical-quality harness over durable review proxies. It adds versioned raw sharpness/exposure/clipping/contrast/resolution evidence, q78 profile-gated candidate reasons, deterministic PostgreSQL proxy sampling, private JSON review export, timing/storage measurements and synthetic analyzer coverage. No collection semantics or catalogue quality persistence are introduced. Keep WI-0182 in review until CI passes and representative private-archive review/runtime evidence is recorded.

The maintainer accepted WI-0163, WI-0167, WI-0169, WI-0170, WI-0171, WI-0172, WI-0173, WI-0175, WI-0176 and WI-0177. On 2026-10-04 (Europe/Stockholm), the maintainer also accepted WI-0179, WI-0180, WI-0181, WI-0183 and WI-0184; issues #487, #488, #489, #491 and #492 are resolved.

## Next concrete step

Run CI for the WI-0185 date-only filter follow-up, then repeat maintained audit verification with a From/To date range. Confirm selected dates persist, Apply filters changes the result set, the complete To date is included, large-result paging remains responsive and duplicate-free, provenance/order look truthful, and correction/return-context behavior still passes. Close out WI-0165 and WI-0178 separately from this follow-up. For WI-0182, merge the evaluation harness after CI, then run `PhotoIdentity.PhotoQualityEvaluation` against a representative private sample, fill the human-review fields, retain measured median/p95 runtime and evidence size, and decide whether the signals justify any separately scoped product use. M30 video support remains deferred.

## Relevant pointers

- docs/delivery/work-items/WI-0165-slideshow-library-status-counts.md
- docs/delivery/work-items/WI-0178-slideshow-library-filtering.md
- docs/delivery/work-items/WI-0185-automatic-assignment-audit.md
- docs/delivery/work-items/WI-0182-photo-quality-scoring-evaluation.md
- docs/operations/photo-quality-evaluation.md
