---
id: M22
title: Protected Smart Collection slideshow
status_source: ../status/milestones.yaml
depends_on: [M18, M19]
---

# M22: Protected Smart Collection slideshow

## Outcome

Photo Identity can present any saved Smart Collection as a stable, oldest-to-newest fullscreen slideshow that is practical to hand to a toddler on a phone. The slideshow has persistent autoplay/end/input/orientation settings, deliberate parent-only controls, orientation/wake protections, bounded image prefetching, observable best-quality original preparation and a read-only slideshow library for basic content consumption.

The complete product contract and cross-cutting acceptance criteria are in [`../../product/slideshow.md`](../../product/slideshow.md).

## Primary use case

A parent opens Photo Identity from a supported phone browser/PWA on the trusted private network, preferably enters the read-only slideshow library, selects a saved Smart Collection and presses **Start slideshow**. Photo Identity immediately requests true fullscreen, snapshots the collection, locks the chosen orientation where the browser permits it, keeps the display awake where supported and begins autoplay when enabled. The child can tap or swipe photos but cannot easily expose normal Photo Identity controls or leave the slideshow through application UI.

Web platform/OS restrictions are explicit: Photo Identity cannot disable Home/app-switch/hardware controls, and orientation/wake APIs may be unavailable or revoked. Unsupported protection must be surfaced to the parent rather than silently represented as active.

## Scope

M22 includes the original five implementation areas plus three real-phone acceptance follow-ups:

- an explicit secure trusted-LAN phone access path while retaining loopback-only packaged defaults;
- an atomic full-collection slideshow snapshot/manifest with deterministic oldest-to-newest ordering;
- the fullscreen playback shell, global persisted slideshow preferences, autoplay/timer/end behavior and bounded prefetch;
- protected toddler mode including deliberate parent unlock, Back/fullscreen recovery, orientation lock and wake lock;
- optional full-snapshot original preparation using the existing bounded OneDrive hydration safety model;
- persisted manual-navigation and explicit orientation preferences;
- observable/recoverable original preparation progress for slow or stalled OneDrive transitions; and
- a read-only slideshow library with standalone original preparation.

V1 starts from saved Smart Collections only. Transient/unsaved Smart Collection previews may be added later without changing the snapshot/playback contract.

## Work items

- [WI-0082](../work-items/WI-0082-secure-mobile-slideshow-access.md) — add an explicit packaged trusted-LAN HTTPS/secure-context path for phone slideshow use without widening the default unauthenticated loopback exposure.
- [WI-0083](../work-items/WI-0083-slideshow-snapshot-manifest.md) — materialize a stable complete Smart Collection slideshow snapshot with deterministic oldest-to-newest ordering and lazy playback resource lookup.
- [WI-0084](../work-items/WI-0084-fullscreen-slideshow-playback.md) — build the fullscreen presentation shell, tap/swipe/keyboard navigation, autoplay, timer progress, end behavior, global browser-local settings and bounded prefetch.
- [WI-0085](../work-items/WI-0085-protected-toddler-slideshow.md) — make protected phone playback the default, including parent unlock, history/fullscreen recovery, orientation lock, wake lock and capability fallbacks.
- [WI-0086](../work-items/WI-0086-slideshow-original-preparation.md) — add explicit best-quality preparation that preflights/reserves the full snapshot and keeps slideshow-owned originals local for uninterrupted playback under existing storage limits.
- [WI-0092](../work-items/WI-0092-slideshow-input-orientation-settings.md) — add a Manual navigation preference and explicit Current/Portrait/Landscape orientation settings.
- [WI-0093](../work-items/WI-0093-slideshow-original-preparation-progress.md) — make preparation progress show downloading/queued/waiting state and provide no-progress retry/cancel recovery.
- [WI-0094](../work-items/WI-0094-read-only-slideshow-library.md) — add the basic-user slideshow library and standalone collection original preparation.

## Delivery sequence

1. WI-0082 establishes a supported phone/secure-context access path and a real-device test target.
2. WI-0083 establishes the immutable slideshow source contract independently of UI paging.
3. WI-0084 builds basic fullscreen slideshow playback on the snapshot contract.
4. WI-0085 hardens that playback for the toddler-first phone use case and is required before V1 acceptance.
5. WI-0086 adds optional best-quality original preparation without weakening existing bounded hydration/ownership rules.
6. WI-0092 closes phone-review input/orientation gaps without changing snapshot semantics.
7. WI-0093 makes OneDrive preparation observable/recoverable before relying on it as a handoff workflow.
8. WI-0094 adds the read-only consumer entry point and reuses the finalized settings/preparation contracts.

WI-0083 and WI-0082 can be implemented independently. WI-0084 depends on the snapshot contract. WI-0085 depends on both working playback and the supported mobile access path. WI-0086 depends on the snapshot/playback lifecycle plus WI-0042's bounded-original semantics.

## Defaults retained by this milestone

- Autoplay: **On**.
- Image duration: **5 seconds**.
- Timer progress: **On**.
- Manual navigation: **On**.
- Orientation: **Current at start**.
- After last photo: **Loop**.
- Protected slideshow: **On**.
- Prepare originals: **Off**.
- Fit: contain/no crop on black background.
- Order: oldest to newest.
- Photo counter: absent in V1.

Global slideshow preferences mean global across Smart Collections and sessions in the same browser/device profile. Cross-device settings synchronization is not part of M22.

## Verification strategy

Automated coverage must validate contracts that do not require a physical browser/device: snapshot consistency/order, saved-collection membership, zero/one-photo behavior, settings serialization/validation, autoplay/end state machine, history/navigation state, resource prefetch bounds and storage/hydration ownership rules.

Browser APIs must sit behind a small JavaScript interop boundary that can report actual capability/acquisition status rather than assuming fullscreen, orientation lock or wake lock succeeded.

Maintainer acceptance must include a real phone on the supported trusted-LAN path. At minimum verify:

- Start slideshow enters fullscreen from the initiating user gesture;
- the snapshot is complete and starts oldest-first;
- repeated taps, left/right swipes and ordinary toddler-like touches cannot expose normal application controls;
- Manual navigation Off prevents tap/swipe/Left/Right photo changes while autoplay continues;
- Portrait/Landscape can be selected from settings and applied on the real phone where orientation locking is supported;
- parent unlock works deliberately and is not triggered by ordinary interaction;
- rotating the phone does not change portrait/landscape when orientation lock succeeds;
- unsupported orientation lock is visibly reported with a system-lock fallback;
- the screen remains awake during a representative autoplay period when Wake Lock is available;
- Back and unexpected fullscreen loss pause/recover without exposing Smart Collections;
- settings persist across exiting/restarting a slideshow;
- Loop, Stop and Exit end behavior work;
- a zero-photo collection and a one-photo collection use the same protected lifecycle;
- Prepare originals shows ready/downloading/queued/waiting state, produces a fully ready session when within policy, and provides actionable no-progress recovery rather than hanging on an opaque counter;
- the read-only slideshow library can start a slideshow and prepare originals without exposing Smart Collection editing.

## Exit criteria

- A saved Smart Collection can start a true-fullscreen slideshow from the complete snapshot rather than the visible UI page.
- Slideshow ordering is deterministic oldest-to-newest and remains stable for the session.
- Autoplay, duration, progress visibility, manual-navigation, orientation, end behavior, protected mode and original preparation settings persist according to the V1 browser-local global-settings contract.
- The fullscreen surface works with tap/click, swipe and desktop keyboard input while keeping browser history bounded to a slideshow entry/exit boundary.
- Protected mode is the default and normal playback has no visible administrative/exit chrome.
- Parent controls require the specified deliberate unlock gesture and Exit requires a second deliberate hold.
- Orientation/wake/fullscreen APIs are capability-detected and failures degrade safely with parent-visible guidance.
- Phone use has a documented opt-in secure-context trusted-LAN path; normal packaged startup remains loopback-only by default.
- Normal slideshow playback never hydrates originals implicitly.
- Best-quality preparation respects the existing free-space/managed-byte/concurrency limits and never releases pre-existing local or user-pinned originals.
- Browser memory remains bounded through lazy loading/prefetch rather than decoding the full collection.
- Original preparation exposes useful aggregate progress/no-progress recovery instead of only a ready count.
- A read-only slideshow library provides the normal basic-user consumption surface and standalone Prepare originals action without claiming to be an authorization boundary.
- Real-phone maintainer verification passes the toddler-safety, configurable input/orientation, wake, recovery, consumer-page and prepared-original checks described above.

## Non-goals

M22 does not add shuffle, transitions, Ken Burns effects, metadata overlays, slideshow editing, per-collection settings, photo counters, transient-preview slideshow entry or an attempt to suppress operating-system Home/app-switch/power gestures.
