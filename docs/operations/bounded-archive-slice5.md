# WI-0042 Slice 5 acceptance corrections

Slice 5 addresses usability and status defects discovered during the combined Windows/OneDrive acceptance for WI-0042. The bounded hydration, ownership, immutable-revision and review-proxy rules from Slices 1–4 remain authoritative.

## Acceptance findings that triggered Slice 5

1. **Manual step advancement does not scale.** The current `Advance archive` action stops when OneDrive is still downloading or releasing content. A folder with many online-only images therefore requires repeated operator clicks even though the required next action is already known.
2. **Collections opens the thumbnail instead of the review proxy.** The collection card and its click target use the compatibility `ContentUrl`, which aliases the fixed 480 × 320 thumbnail. The API already exposes separate thumbnail, preview and original URLs, but the UI does not provide a proxy-first viewer or explicit original controls.
3. **Analysis and availability are conflated in Archive drill-down.** A successfully analysed revision returned to `OnlineOnly` can disappear from the `Analyzed` filter even though faces, embeddings and proxy state remain durable.

These findings pause the WI-0042/WI-0041 human gate. They are product acceptance blockers, not evidence that the underlying persisted analysis or proxy data was lost.

## Slice 5 operator model

The intended archive workflow becomes:

```text
Add folder
-> Advance archive once
-> synchronize included coverage
-> continue unattended while work is actionable
   -> source verification
   -> bounded hydration
   -> wait for OneDrive transitions
   -> exact-profile analysis
   -> durable proxy generation
   -> managed release
   -> next image
-> Complete, Paused or Blocked
```

The operator may still run `Sync included folders` independently when discovery-only behavior is desired.

### Durable advancement

Archive advancement is server-owned rather than browser-click-owned. Starting advancement records durable intent in SQLite so an interrupted application can resume the same operation after restart. The worker automatically rechecks OneDrive while managed content is downloading or releasing and continues until:

- all intended archive work is complete;
- the operator pauses the run; or
- a real error or storage-policy condition requires operator intervention.

The existing free-space reserve, managed-byte budget, concurrency limits, LRU release policy and ownership rules remain unchanged. Slice 5 must not release pre-existing local or user-pinned originals.

## Photo viewer

Normal photo viewing is proxy-first:

- collection grids continue to use the compact thumbnail for efficient paging;
- opening a photo displays the durable review proxy through the `preview` route;
- the same viewer is reachable from Archive drill-down when a current revision exists;
- normal proxy viewing never requests original hydration.

Full-resolution access is explicit. The viewer exposes original state and actions backed by the existing API:

```text
online-only -> Load original -> downloading -> ready -> Open original
ready + managed -> Release original -> releasing -> online-only
```

A pre-existing local or user-pinned original may be opened but remains unmanaged and cannot be automatically released by Photo Identity.

## Orthogonal archive status

Archive item state is presented as three independent dimensions:

1. **Availability:** local, online-only, downloading, unavailable or error.
2. **Source verification:** verified, needs verification or first verification.
3. **Analysis:** analyzed, pending, failed or not ready.

The normal bounded-storage steady state is therefore representable as:

```text
Online-only | Verified | Analyzed
```

Returning an analysed original to online-only must not remove it from the analyzed filter or change durable face/embedding/proxy completion.

The folder summary should also stop combining first verification and re-verification into one ambiguous `Verify` count.

## Slice 5 acceptance checks

Before WI-0042 acceptance resumes, verify locally that:

- one `Advance archive` action can process a folder containing many online-only images without per-transition clicks;
- the operation automatically waits for OneDrive downloads/releases and continues afterward;
- pause/restart/resume preserves durable advancement intent;
- normal collection/archive photo viewing uses the review proxy and leaves an online-only original online-only;
- explicit original hydration exposes downloading/ready/release state and never happens as a side effect of proxy viewing;
- an analysed revision remains `Analyzed` after `Local -> OnlineOnly` while availability reports `Online-only`;
- separate availability/source-verification/analysis filters can select that state; and
- all existing bounded storage, ownership and no-repeat inference acceptance checks still pass.

Only path-free pass/fail and aggregate evidence belong in repository documentation.