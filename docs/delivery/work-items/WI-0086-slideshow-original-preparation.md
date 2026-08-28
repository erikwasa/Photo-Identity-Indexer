---
id: WI-0086
title: Prepare and retain slideshow originals for best-quality playback
milestone: M22
status_source: ../status/work-items.yaml
depends_on: [WI-0042, WI-0083, WI-0084]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Web, documentation]
---

# WI-0086: Prepare and retain slideshow originals for best-quality playback

## Objective

Add an explicit **Prepare originals** slideshow mode that can deliver uninterrupted best-quality playback from the complete snapshot without weakening Photo Identity's existing bounded OneDrive hydration, immutable revision verification or ownership/release rules.

Normal slideshow playback remains proxy/local-original based and never hydrates implicitly.

## Contract

When `Prepare originals` is Off:

- use normal viewer-preview behavior;
- prefer an already-local verified original when available;
- otherwise use the durable review proxy where available;
- never request OneDrive hydration solely because slideshow playback reached an online-only item.

When `Prepare originals` is On:

1. Create the WI-0083 immutable slideshow snapshot.
2. Resolve original status/logical size for the entire snapshot without opening online-only placeholders.
3. Preflight the complete session against configured minimum-free-space and maximum-managed-hydration limits.
4. If necessary, use the existing managed LRU policy to request release of eligible non-session app-owned content, and wait for observed release before admitting preparation.
5. Reserve/protect the active slideshow set so later preparation in the same session cannot evict an earlier prepared slideshow original.
6. Hydrate required online-only originals with bounded concurrency.
7. Verify each local original against the immutable catalogue revision before marking it ready.
8. Start autoplay only when all required originals are ready, unless the parent explicitly accepts ordinary available/proxy playback after a reported failure.
9. Keep active-session prepared originals protected from managed eviction until the slideshow exits.
10. On exit, remove slideshow-specific protection. App-owned prepared content may remain local and return to normal managed-LRU eligibility rather than being forcibly released immediately.

## Full-snapshot admission

Best-quality preparation is an all-session promise. Do not begin a large set of downloads and then silently discover that the complete snapshot can never fit under configured policy.

Preflight must account for:

- originals already local before Photo Identity ownership (zero new managed reservation and never release them);
- originals already local/downloading under existing Photo Identity ownership;
- newly requested online-only originals and their full logical byte lengths;
- current managed reserved bytes;
- configured maximum managed hydration bytes;
- configured minimum free-space reserve;
- release-in-progress bytes, which remain reserved until OneDrive is observed online-only.

If the full snapshot cannot be admitted, return an actionable path-free summary before autoplay:

```text
Best-quality slideshow cannot prepare all originals under the current storage policy.
Required additional bytes: <aggregate>
Available managed capacity: <aggregate>

Start with available/proxy images
Cancel
```

Do not expose filenames/source paths in this summary.

## Session protection / ownership

The existing hydration ownership rules remain authoritative:

- never claim a file that was already local or user-pinned;
- never release pre-existing local/user-pinned content;
- only Photo-Identity-owned hydration can enter managed release/LRU behavior;
- revision length/SHA-256 verification remains required before serving an authoritative original.

Add the minimum durable/ephemeral lease state necessary to distinguish originals protected by an **active slideshow session** from ordinary managed content. Capacity eviction must skip active slideshow leases.

A process crash/restart must not create permanent unreleasable ownership. Existing durable ownership remains recoverable; stale slideshow-specific protection must expire/reconcile safely because the slideshow session itself is not durable V1 state.

## Preparation UI

Fullscreen preparation stays inside the slideshow shell and may show privacy-safe aggregate progress such as:

```text
Preparing slideshow
187 / 250 photos ready
```

It may also show aggregate bytes if useful. Do not display source paths.

Autoplay remains paused during preparation. The parent can open protected controls to cancel or, after a preparation failure, deliberately continue with ordinary available/proxy playback.

## Playback resource behavior

Once preparation succeeds, playback should request verified original resources for the active session rather than lower-resolution proxies. Image decoding/prefetch in the browser remains bounded; preparing originals refers to local source availability, not decoding every photo into browser memory.

If a supposedly prepared original later fails immutable verification or becomes unavailable, pause and surface a parent-visible error/fallback choice rather than silently presenting the session as all-original quality.

## Acceptance criteria

- [ ] With Prepare originals Off, slideshow playback never invokes original hydration.
- [ ] With Prepare originals On, the complete immutable snapshot is preflighted before autoplay begins.
- [ ] Full-snapshot preflight counts only the appropriate managed/incremental bytes and respects minimum free space plus maximum managed hydration policy.
- [ ] A snapshot that cannot fit is rejected before silently starting a partial best-quality session.
- [ ] Eligible non-session managed content may be evicted through the existing LRU/release process, and release-requested bytes remain counted until observed online-only.
- [ ] Active slideshow originals are protected from same-session LRU eviction while the slideshow is running.
- [ ] Hydration uses the existing bounded concurrent operation policy.
- [ ] Every prepared original is size/SHA-256 verified against its immutable revision before best-quality playback marks it ready.
- [ ] Autoplay waits for successful complete preparation, or for an explicit parent decision to continue with ordinary available/proxy content after failure.
- [ ] Pre-existing local/user-pinned originals are never claimed or released by slideshow preparation.
- [ ] Deliberate slideshow exit removes slideshow-specific eviction protection without requiring immediate release of app-owned prepared content.
- [ ] Stale/crashed slideshow leases cannot permanently strand managed content as non-evictable.
- [ ] Preparation/progress/error responses are aggregate/path-free.
- [ ] Successful prepared playback uses verified originals while browser image decoding remains bounded to the normal prefetch window.
- [ ] Focused integration tests cover admission success/failure, existing-local content, app-owned content, LRU interaction, active-session eviction protection, immutable verification failure, crash/stale-lease reconciliation and normal-mode no-hydration behavior.
- [ ] Maintainer verification includes one real Smart Collection containing a mixture of already-local and online-only originals and confirms uninterrupted original-quality playback after preparation.

## Non-goals

- Removing or weakening WI-0042 hydration limits.
- Automatically hydrating originals when Prepare originals is Off.
- Permanent per-collection offline albums.
- Keeping every archive original local indefinitely.
- Decoding/caching the entire slideshow in browser memory.
