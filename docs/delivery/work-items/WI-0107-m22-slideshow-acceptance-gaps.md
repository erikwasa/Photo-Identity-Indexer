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

## Acceptance criteria

- [ ] Pressing **Start slideshow** on `/slideshows` requests fullscreen directly from the initiating click/tap before navigation or awaited work.
- [ ] On the supported real phone/browser, a successful fullscreen request produces no intermediate **Enter fullscreen** application step.
- [ ] Snapshot/original preparation loading states may be shown after the click, but they are shown inside fullscreen.
- [ ] Fullscreen rejection/unsupported capability still lands on the safe recovery surface.
- [ ] A successful standalone preparation remains visibly **Originals prepared** after starting and deliberately exiting a slideshow when the exact prepared set remains reusable.
- [ ] The prepared-state representation survives recreation of the `/slideshows` component in the same browser/device profile.
- [ ] The prepared-state representation is revalidated and is removed/downgraded when the relevant originals are no longer reusable or collection membership has changed.
- [ ] Successful standalone preparation still releases temporary slideshow-preparation protection and does not become a permanent offline pin.
- [ ] Persisted preparation state is path-free.
- [ ] Automated tests cover the originating-gesture fullscreen handoff and prepared-state persistence/revalidation lifecycle.
- [ ] Maintainer re-verification on the real phone passes these two remaining M22 scenarios.

## Non-goals

- Slideshow-library query performance.
- Snapshot/startup latency optimization.
- Image-to-image latency optimization.
- Permanent per-collection offline pinning.
- Changing the managed hydration/LRU ownership policy.
