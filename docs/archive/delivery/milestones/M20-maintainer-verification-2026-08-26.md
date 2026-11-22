# M19/M20 final maintainer verification — 2026-08-26

## Purpose

Record the maintainer's final consolidated browser/operator verification after the M20 corrective PRs and GeoNames pacing compatibility fix were merged to `main`.

This document supersedes the remaining `PENDING`, `INCOMPLETE` and unchecked maintainer-verification markers in the earlier M19/M20 review/checklist documents for lifecycle purposes. Those older documents remain useful as historical test plans and correction contracts.

## Baseline

PR #205 was merged to `main` as merge commit `a0548171daf2230d929069e49337fcf9a585cbb5`. The post-merge comprehensive workflow #1244 (`32528282922`) completed successfully, including build/fast tests, quarantined diagnostics, both required integration shards, published review verification, launcher verification and Windows package verification.

The maintainer then ran the consolidated real-application checklist against current `main` and reported that **everything in the planned review passed**.

## Final acceptance results

### WI-0072 — Archive photo metadata integration

**PASS.** Real archive processing verified the remaining maintainer-only behavior:

- newly included archive media is metadata-inspected through the normal archive lifecycle without manually invoking metadata backfill;
- representative JPEG and iPhone HEIC/HEIF metadata is available in Photo Details;
- capture/camera/GPS behavior is correct for the tested media;
- persisted GPS flows into automatic GeoNames enrichment rather than requiring a manual browser batch;
- metadata inspection does not introduce a separate unsafe hydration contract.

WI-0072 is accepted for completion.

### WI-0064 / WI-0065 — GeoNames provider, automatic orchestration and language policy

**PASS.** The consolidated pass verified the remaining live-provider/operator behavior:

- automatic GPS-bearing revisions are picked up without pressing a maintenance Enrich action;
- enrichment remains independent of archive processing/browser request lifetime;
- restart/resume works from durable catalogue state;
- manual Place/manual-clear precedence remains intact;
- Sweden retains local-language naming;
- non-Swedish samples use English naming under the revised policy;
- policy-aware cache behavior does not require repeated duplicate foreign-coordinate lookups in normal reuse;
- provider/backoff behavior remains compatible with the corrected normal pacing contract.

WI-0064 and WI-0065 are accepted for completion, closing the remaining M19 verification gap.

### WI-0073 — UI/navigation polish and archive advancement classification

**PASS.** The maintainer verified the corrective slice:

- Face Review suggested-person results are collapsed initially and operate as a compact searchable picker;
- Queue controls are contained/denser;
- hidden people use compact `Hidden` presentation;
- Smart Collection selected/available-person rows keep representative portraits and Add/Remove actions contained when lists scroll;
- Maintain People removes redundant availability/representative-selection copy, uses `Hide`/`Show`, and reports distinct photo counts;
- archive state remains Running while useful work can progress alongside OneDrive transitions and uses Waiting only when OneDrive is the sole blocker;
- previously accepted menu dismissal, favorite type-ahead and archive return-context behavior remains intact.

WI-0073 is accepted for completion.

### WI-0074 — Suggested-person Face Review filtering

**PASS retained.** This item had already passed the 2026-08-21 maintainer review. The final consolidated pass found no regression. WI-0074 is accepted for completion.

### WI-0075 — GeoNames timing settings

**PASS.** The maintainer verified the corrected timing contract, including explicit below-30000 automatic timing overrides and diagnostics that reflect the actual effective provider/automatic pacing. The 30000 ms value is a default, not a hard minimum; explicit raw provider pacing still participates in the effective gate; provider-directed backoff can delay longer than normal pacing.

WI-0075 is accepted for completion.

### WI-0077 — Photo Details presentation

**PASS.** The maintainer verified the corrective Photo Details layout:

- compact desktop metadata layout with narrow-screen fallback;
- Photo taken/Camera/GPS presentation is readable without unnecessary wrapping/full-width GPS;
- Location appears directly after capture metadata/GPS and before People;
- read mode uses the compact city + most-specific-locality presentation where semantic data supports it;
- the complete canonical Place hierarchy remains available for editing/querying;
- assigned/unassigned Place and edit/cancel/mutation flows remain correct;
- collapsed All metadata retains secondary technical fields.

WI-0077 is accepted for completion.

### WI-0078 — Versioned metadata refresh

**PASS retained.** This item had already passed real-catalogue maintainer verification on 2026-08-21. The final consolidated pass found no regression. WI-0078 is accepted for completion.

## Lifecycle outcome

- M19's remaining open verification items (WI-0064, WI-0065 and WI-0072) are now accepted. M19 can be marked **completed**.
- WI-0073, WI-0074, WI-0075, WI-0077 and WI-0078 are accepted for completion.
- WI-0076 remains separate archive-throughput work; PR #200 is not part of this acceptance result and still requires its own merge/performance verification path.
- M20 therefore remains active until WI-0076 is resolved.

## New issues found after the successful checklist

The maintainer reported three separate issues after confirming the planned checklist passed. They are **new follow-up work** and do not invalidate the acceptance results above.

### Critical — included-folder synchronization timeout

**Observed:** clicking **Sync included folders** is taking increasingly long as the real catalogue grows. The most recent attempts have failed with:

```text
net_http_request_timedout, 100
```

This is tracked as [WI-0079](../work-items/WI-0079-included-folder-sync-timeout.md). The first follow-up investigation must determine scaling, phase timings and request-cancellation behavior before choosing a fix.

### High — ambiguous detected face when two faces are visible

**Observed:** some face-detection/review images contain two visible faces, and the current image does not always make it obvious which face is the actual detected/review target.

This is tracked as [WI-0080](../work-items/WI-0080-detected-face-clarity.md). The investigation must trace detection geometry through the displayed derivative and compare target-indication treatments before implementation.

### Medium — identity suggestion accuracy appears to be degrading

**Observed:** suggestion accuracy appears to be getting worse as the real catalogue/reference corpus grows.

This is tracked as [WI-0081](../work-items/WI-0081-suggestion-accuracy-degradation.md). The investigation must quantify the regression, audit reference/evidence quality and compare mitigation families before changing thresholds, ranking or models.

## Follow-up decision

The three new issues are grouped into **M21 — Reliability and recognition quality**. Investigation and solution selection will happen in a separate development conversation. No product-code fix is selected or authorized by this verification document.
