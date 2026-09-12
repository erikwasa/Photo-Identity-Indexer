---
id: WI-0107
title: Close final M22 slideshow launch and prepared-state acceptance gaps
milestone: M22
status_source: ../status/work-items.yaml
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api, documentation, testing]
---

# WI-0107: Close final M22 slideshow launch and prepared-state acceptance gaps

## Objective

Close the two functional gaps found during the consolidated real-phone M22 acceptance after the M22 implementation and corrective package/preparation work had merged.

The maintainer accepted the remaining slideshow behavior, but reported:

1. **Start slideshow** from the read-only slideshow library does not immediately enter fullscreen and begin the slideshow lifecycle. An intermediate fullscreen recovery step is shown.
2. A collection that successfully completed standalone **Prepare originals** no longer appears prepared after entering/exiting a slideshow, even when the prepared originals are still local and reusable.

These are M22 product-contract issues. They are separate from the slideshow performance findings tracked under M24.

## Contract

### Direct fullscreen launch

The initiating **Start slideshow** user action must request true browser fullscreen synchronously from that same user gesture, before navigation, snapshot creation, original preparation or other awaited work.

On a browser that accepts fullscreen:

- there is no intermediate application step asking the user to enter fullscreen;
- navigation/loading/preparation proceeds inside the already-fullscreen slideshow surface;
- autoplay begins as soon as the slideshow is ready when Autoplay is enabled.

If fullscreen is unsupported or the browser rejects the request, the existing safe recovery surface remains valid. The application must not falsely report fullscreen as active.

The behavior must apply to the normal `/slideshows` Start slideshow action and remain compatible with other supported slideshow entry points.

### Prepared-original state across navigation

Successful standalone preparation is not a permanent offline pin. Preparation-specific protection is still released after success, and Photo-Identity-owned hydrated files remain subject to the managed LRU policy.

However, leaving `/slideshows`, playing the slideshow and returning must not discard the successful preparation result merely because the page component was recreated.

The read-only slideshow library should retain a path-free preparation receipt sufficient to re-establish the latest successful preparation state for that browser/device profile. The UI must revalidate that the relevant prepared snapshot is still reusable before presenting **Originals prepared** as current.

If one or more prepared originals have actually become unavailable/online-only or the current collection snapshot has changed, the UI should downgrade the prepared state rather than show a stale success badge.

The receipt must not contain source paths, filenames or other private source locators.

### Browser-unsupported prepared originals

Preparation readiness and browser decode support are separate concerns.

- A `ready` original means its authoritative bytes are local and verified against the immutable catalogue revision.
- Browser renderability must not turn that verified state into a preparation failure.
- During prepared playback, browser-supported media should use the verified original.
- If a verified original uses a media type the browser cannot render directly, playback should use the existing durable viewer/review proxy fallback rather than fail the entire prepared session or send unsupported bytes to the browser.
- Storage admission, hydration ownership, revision verification and receipt revalidation continue to reason about the authoritative original; the proxy is only the browser presentation fallback.

## Implementation — 2026-09-12

The corrective slice keeps the existing slideshow lifecycle and hydration ownership boundaries intact:

- `/slideshows` now uses the same originating-gesture fullscreen handoff already used by the Smart Collections workspace. The fullscreen request is the first asynchronous browser action from the Start slideshow click/tap and navigation follows only after that request completes or throws. A rejection still navigates to the slideshow, where the existing fullscreen recovery surface remains authoritative.
- Standalone preparation now records the exact immutable revision-ID set while its temporary server session is active. Active browser-local bookmarks were versioned so a preparation that completes after navigation can still produce the final receipt.
- A successful preparation still ends the server preparation session immediately, releasing slideshow-preparation protection. The persistent browser receipt contains only immutable revision IDs; it does not retain a lease, source path, filename or original URL.
- When `/slideshows` is recreated, a receipt is not shown as ready immediately. The page creates a fresh snapshot for that saved collection, requires the exact revision set to match, then calls a non-hydrating revalidation endpoint. That endpoint reports reusable only when every recorded revision is still local and revision-verified.
- Changed collection membership, an online-only/released original, hash mismatch or another non-ready original removes the current prepared badge. A transient inability to revalidate does not display stale success; the receipt is retained only so a later page load can try again.
- No permanent offline pinning, hydration-policy change or slideshow performance work is included.

Focused automated coverage verifies the fullscreen-before-navigation ordering, safe navigation after fullscreen interop rejection, path-free/set-matching receipt behavior, and server revalidation downgrade after an original becomes online-only.

## Real-phone follow-up — 2026-09-13

After PR #314 merged, maintainer verification found a third acceptance blocker while running standalone **Prepare originals**:

```text
Preparation needs attention
One or more verified originals cannot be rendered directly by this browser. Continue with available/proxy images instead.
Required additional bytes: 24403558
Available managed capacity: 49960944424
```

The capacity figures show that storage admission was not the blocker. The preparation service reached a locally verified original whose media type was not directly browser-renderable and incorrectly treated `CanView=false` as a fatal preparation error.

PR #315 corrects that boundary:

- locally verified originals are accepted as prepared regardless of browser decode support;
- immediately hydrated originals are likewise accepted once they reach verified `ready` state;
- the active-session prepared-original endpoint preserves the session/lease/verification check, then redirects browser-unsupported media to the existing `viewer-preview` path;
- `viewer-preview` already serves supported verified originals directly and durable JPEG proxies for unsupported media such as HEIC;
- an end-to-end regression test covers a verified `image/heic` original with a durable JPEG proxy and requires preparation to reach `ready` plus prepared playback to return the proxy.

## Acceptance criteria

- [x] Pressing **Start slideshow** on `/slideshows` requests fullscreen directly from the initiating click/tap before navigation or awaited work.
- [ ] On the supported real phone/browser, a successful fullscreen request produces no intermediate **Enter fullscreen** application step.
- [x] Snapshot/original preparation loading states may be shown after the click, but they are shown inside fullscreen.
- [x] Fullscreen rejection/unsupported capability still lands on the safe recovery surface.
- [x] A successful standalone preparation remains visibly **Originals prepared** after starting and deliberately exiting a slideshow when the exact prepared set remains reusable.
- [x] The prepared-state representation survives recreation of the `/slideshows` component in the same browser/device profile.
- [x] The prepared-state representation is revalidated and is removed/downgraded when the relevant originals are no longer reusable or collection membership has changed.
- [x] Successful standalone preparation still releases temporary slideshow-preparation protection and does not become a permanent offline pin.
- [x] Persisted preparation state is path-free.
- [x] Automated tests cover the originating-gesture fullscreen handoff and prepared-state persistence/revalidation lifecycle.
- [x] A local, revision-verified browser-unsupported original does not fail preparation, and prepared playback falls back to its durable proxy instead of returning unsupported original bytes.
- [ ] The real-phone collection that previously triggered the browser-format warning now completes **Prepare originals** and displays those photos through the proxy fallback.
- [ ] Maintainer re-verification on the real phone passes these remaining M22 scenarios.

## Maintainer verification remaining

After PR #315 is green and merged, verify from the supported phone/browser:

1. Open `/slideshows` and rerun **Prepare originals** on the same collection that previously reported `One or more verified originals cannot be rendered directly by this browser`. Confirm preparation completes and the collection shows **Originals prepared**.
2. Start that slideshow and confirm photos with browser-unsupported original formats are still displayed through the durable proxy rather than producing an image error.
3. Confirm browser-supported prepared photos continue to display from their verified originals.
4. With Autoplay enabled, press **Start slideshow** and confirm fullscreen is entered directly from that tap, loading/preparation remains inside fullscreen and no application **Enter fullscreen** step appears when the browser accepts fullscreen.
5. Deliberately exit back to `/slideshows` and confirm **Originals prepared** is restored when those exact originals remain local.
6. If practical, make one prepared original online-only or change the collection membership, reload `/slideshows`, and confirm the stale prepared badge is no longer shown.

## Non-goals

- Slideshow-library query performance.
- Snapshot/startup latency optimization.
- Image-to-image latency optimization.
- Permanent per-collection offline pinning.
- Changing the managed hydration/LRU ownership policy.
