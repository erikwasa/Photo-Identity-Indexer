# Protected Smart Collection slideshow

## Purpose

Photo Identity should be able to present a saved Smart Collection as a fullscreen slideshow. The primary V1 use case is a parent handing a phone to a toddler, so accidental exit resistance, stable orientation and uninterrupted playback are product requirements rather than optional polish.

The slideshow remains a read-only presentation surface. It does not edit people, tags, Places, favorites or other catalogue metadata.

## V1 product decisions

- A slideshow starts from a **saved Smart Collection**. Supporting an unsaved/transient Smart Collection preview is deferred because saved collections already provide a durable source identity.
- Starting a slideshow creates a **snapshot** of the collection. Changes to the collection definition, tags, people, Places or catalogue membership after start do not alter the running session.
- Snapshot order defaults to **oldest to newest**. Use photographic capture time when available and catalogue observation time as the fallback, with a deterministic immutable-revision tie break.
- The Start slideshow user action immediately requests **true browser fullscreen** before awaiting snapshot preparation or other asynchronous work.
- Photos are centered on a black presentation surface and use contain/no-crop fit in V1.
- Autoplay starts immediately when the persisted Autoplay setting is enabled.
- Manual next/previous navigation is controlled by a persisted Manual navigation setting; when enabled, manual navigation resets the current image timer.
- There is no photo-number counter in V1.
- Zero-photo and one-photo snapshots enter the same slideshow shell and use the same fullscreen/protected-exit lifecycle as larger collections.

## Playback controls

The presentation surface intentionally has almost no normal controls.

- Tap/click on the presentation surface advances to the next photo.
- Swipe left advances to the next photo.
- Swipe right returns to the previous photo.
- Desktop Left/Right Arrow navigate previous/next.
- Desktop Space toggles play/pause.
- The optional autoplay timer progress bar is display-only and is not an interactive scrubber.
- Image dragging, text selection, context-menu gestures and browser zoom/pinch interaction should be suppressed on the slideshow surface where the browser permits it.

A manual navigation action restarts the autoplay timer only after the destination image is displayed. Loading time is not charged against the configured image duration.

## Persistent slideshow settings

V1 settings are global across collections and slideshow sessions **within the same browser/device profile**. They are not per-collection and do not require catalogue persistence or cross-device synchronization.

| Setting | V1 default | Contract |
|---|---|---|
| Autoplay | On | Start advancing automatically as soon as playback is ready. |
| Image duration | 5 seconds | Persist a validated duration; V1 should support at least 1-60 seconds. |
| Show timer progress | On | Show only while autoplay is active. |
| Manual navigation | On | When Off, tap/click, swipe and Left/Right Arrow do not move between photos; autoplay and parent controls remain available. |
| Orientation | Current at start | Choices: Current at start, Portrait, Landscape. Apply changes immediately where the browser permits orientation locking. |
| After last photo | Loop | Choices: Loop, Stop on last photo, Exit slideshow. |
| Protected slideshow | On | Hide ordinary exit/settings controls and require the parent unlock gesture. |
| Prepare originals | Off | Explicitly prepare and retain best-quality originals for uninterrupted playback when storage policy permits. |

`Exit slideshow` is intentionally available as an end behavior but is not the toddler-safe default. Loop is the default.

## Protected slideshow mode

Protected mode is designed to make accidental application exit difficult without claiming that a web application can disable operating-system Home, app-switching, notification or hardware-button behavior.

During protected playback:

- there is no visible Exit button, settings gear, previous/next button or other tappable application chrome;
- ordinary taps and swipes can only navigate slideshow content;
- browser Back must not immediately expose Smart Collections or another administrative Photo Identity page;
- unexpected fullscreen loss must leave the user on a harmless paused slideshow recovery surface rather than revealing normal application UI;
- re-entering fullscreen requires an explicit user gesture when required by the browser;
- safe-area insets and operating-system gesture edges must be respected so hidden controls are not placed directly on system gesture zones.

### Parent unlock gesture

On touch devices, simultaneously hold the hidden upper-left and upper-right parent zones for approximately two seconds. The zones are inset inside the device safe area. A successful hold opens parent controls; releasing early does nothing.

Parent controls contain play/pause, settings and Exit slideshow. Exit itself requires a second deliberate press-and-hold confirmation of approximately 1.5 seconds.

On desktop, `Ctrl+Shift+X` opens the same parent controls so protected mode remains operable without multi-touch.

The gesture constants should be isolated in one implementation location so maintainer testing can tune them without changing the slideshow contract.

## Orientation and wake behavior

Orientation is a persisted slideshow preference with **Current at start**, **Portrait** and **Landscape**. Current at start is the backward-compatible default: capture the current screen orientation when slideshow starts and attempt an exact lock after fullscreen entry, with matching portrait/landscape-family fallback. Portrait and Landscape explicitly request those orientation families.

Changing the orientation preference from parent settings during an active fullscreen slideshow should replace the application-owned lock immediately without changing slideshow membership or current position. The browser may rotate the viewport to satisfy the selected family.

Browser support for `ScreenOrientation.lock()` is not universal and commonly depends on fullscreen/mobile context. The implementation must use capability detection. If the requested lock cannot be acquired, playback may continue only after showing a parent-facing warning that system rotation lock should be enabled for toddler use; the failure must not silently pretend orientation is protected.

While a slideshow is active, request a screen wake lock where supported. If the browser/system releases it while the document is hidden or for system reasons, attempt to reacquire it when the document becomes visible again. Failure to acquire a wake lock is non-fatal but must be visible to the parent before handoff.

Relevant platform references:

- <https://developer.mozilla.org/en-US/docs/Web/API/Fullscreen_API>
- <https://developer.mozilla.org/en-US/docs/Web/API/ScreenOrientation/lock>
- <https://developer.mozilla.org/en-US/docs/Web/API/Screen_Wake_Lock_API>

## Supported phone access and secure-context requirement

The current packaged launcher is loopback-only, while phone use necessarily arrives from another device. M22 therefore includes an explicit mobile-access prerequisite rather than silently widening the existing unauthenticated host.

The supported phone path must:

- remain opt-in; default packaged operation stays loopback-only;
- use a trusted private network with narrowly scoped firewall exposure;
- provide an HTTPS/secure-context origin suitable for service worker/PWA and Screen Wake Lock APIs;
- never commit certificates, passwords, private hostnames or addresses to the repository;
- document browser/device capability verification before a phone is handed to a child.

Photo Identity remains unauthenticated under the current trust model. Enabling remote access is therefore a deliberate operator action, not an automatic default.

## Read-only slideshow library

Provide a dedicated basic-user slideshow surface at `/slideshows` that lists saved Smart Collections and exposes only consumption-oriented actions:

- Start slideshow;
- Prepare originals;
- preparation status/recovery; and
- the same global slideshow settings used during playback.

The surface must not expose Smart Collection definition editing/deletion or photo metadata/tag/people/Places mutation controls. It should use a minimal consumer-oriented layout rather than normal operator navigation.

This is a **read-only UI boundary, not an authorization boundary**. Photo Identity remains unauthenticated on the trusted-LAN path, so a knowledgeable user who deliberately visits operator URLs can still reach them until a future authentication/role milestone changes that trust model.

Starting a slideshow from the slideshow library should return there on deliberate Exit.

## Snapshot contract

A slideshow snapshot represents the complete Smart Collection result set at one logical point in time, not the current 40-item UI page and not a series of live offset queries.

The snapshot contains enough immutable identity to preserve order for the session, at minimum:

- Smart Collection identity and display name;
- the ordered immutable revision IDs;
- total snapshot count.

The complete ordered ID manifest should be materialized by one logical database query/transaction so concurrent collection changes cannot shift later offset pages. Resource metadata and pixels may then be loaded lazily.

The slideshow must not decode or retain every image in browser memory. Keep the current image plus a small bounded previous/next prefetch window and request additional resource metadata only as required.

## Best-quality original preparation

Normal slideshow playback follows the existing viewer boundary: use an already-local verified original when available and otherwise use the durable review proxy without implicitly hydrating the archive.

When **Prepare originals** is enabled, slideshow start becomes an explicit hydration operation:

1. Create the immutable slideshow snapshot.
2. Preflight the complete snapshot against the existing bounded hydration/free-space policy.
3. Reserve/protect the active slideshow's app-owned hydration so preparing later items cannot evict earlier items in the same active slideshow.
4. Hydrate and revision-verify required originals in bounded batches.
5. Do not begin autoplay until every required original is ready, or until the parent explicitly chooses to continue with available/proxy content after a reported failure.
6. Keep the active slideshow originals protected for the duration of the session.
7. On exit, end the slideshow-specific protection. Pre-existing local/user-pinned content must never be claimed or released by the slideshow. App-owned prepared content may remain local and re-enter the existing managed LRU policy rather than being forcibly released immediately.

If the entire snapshot cannot fit within configured managed-hydration/free-space limits, fail the full-quality preflight before silently starting a partial best-quality session. The parent may choose ordinary available/proxy playback instead.

Preparation progress must distinguish at least ready/verified, actively downloading, queued online-only and waiting-for-release work. A ready-only counter is insufficient because bounded concurrency can legitimately leave the ready count unchanged while OneDrive downloads are active. If aggregate state makes no progress for a conservative centralized threshold, surface a parent-visible no-progress warning with Retry preparation and Cancel preparation rather than leaving an opaque counter indefinitely.

The read-only slideshow library may start the same full-snapshot preparation without entering fullscreen or starting playback. Successful standalone preparation releases temporary preparation protection when complete but leaves Photo-Identity-owned hydrated originals local and eligible for normal managed LRU behavior. A later slideshow remains authoritative for its own new snapshot.

## End behavior

- **Loop**: after the last photo, continue with the first. For a one-photo snapshot, the same photo remains displayed and its timer cycles without a visible reload flash.
- **Stop on last photo**: pause autoplay and leave the last image displayed indefinitely.
- **Exit slideshow**: end the slideshow and return to the originating Smart Collection context.
- A zero-photo snapshot displays a neutral `No photos are currently in this collection` presentation state. It does not auto-exit.

## History and lifecycle

Starting a slideshow creates one navigation boundary. Advancing photos must not add browser-history entries. Browser Back during protected mode is intercepted into the protected parent/recovery flow rather than exposing the application. A deliberate Exit returns to the originating saved Smart Collection state.

If the document becomes hidden, autoplay pauses and the active timer is preserved. When visible again, the wake lock is reacquired where possible; autoplay resumes according to the prior play/pause state only after the current photo is display-ready.

## V1 acceptance criteria

- [ ] A saved Smart Collection exposes Start slideshow even when it currently contains zero or one photo.
- [ ] The Start slideshow activation requests browser fullscreen synchronously before asynchronous snapshot/network work can consume the user-activation gesture.
- [ ] A running slideshow is based on one stable full-collection snapshot and is unaffected by later Smart Collection/catalogue changes.
- [ ] The snapshot uses deterministic oldest-to-newest ordering with capture time preferred and observed time used as fallback.
- [ ] Presentation uses a black fullscreen surface and contain/no-crop image fit.
- [ ] Manual navigation defaults On; when Off, tap/click, swipe and Left/Right Arrow do not move between photos while autoplay and parent controls still work.
- [ ] When Manual navigation is On, tap/click, swipe and desktop keyboard navigation work as specified and do not create per-photo browser-history entries.
- [ ] Autoplay begins immediately when enabled, duration is persisted, and manual navigation restarts the timer after the destination image is displayed.
- [ ] Timer progress can be hidden and is shown only when autoplay is active.
- [ ] Loop, Stop and Exit end behaviors work; Loop is the default.
- [ ] Settings persist globally across collections/sessions in the same browser/device profile.
- [ ] Protected mode is enabled by default and normal playback exposes no visible exit/settings/navigation chrome.
- [ ] The two-corner parent gesture opens controls only after the hold threshold; Exit requires the second hold confirmation.
- [ ] Browser Back or unexpected fullscreen loss cannot directly expose normal application pages while protected playback is active.
- [ ] Orientation persists as Current at start, Portrait or Landscape; changing it during active fullscreen replaces the requested lock without changing slideshow position.
- [ ] Current-at-start/exact and explicit Portrait/Landscape locks are capability-detected; unsupported/failed lock is surfaced to the parent with a system-rotation-lock fallback instruction.
- [ ] A screen wake lock is acquired/reacquired where supported and failures are surfaced without crashing playback.
- [ ] The supported phone access path is opt-in, secure-context HTTPS and preserves loopback-only default behavior.
- [ ] Normal playback does not implicitly hydrate online-only originals.
- [ ] Prepare originals preflights the complete snapshot, respects existing bounded storage policy and protects active-session originals from same-session eviction.
- [ ] Preparation status distinguishes ready/downloading/queued/waiting work and surfaces an actionable no-progress state instead of an indefinitely opaque counter.
- [ ] A read-only slideshow library lists saved Smart Collections, shares the global slideshow settings, starts slideshows and can prepare originals without entering fullscreen or exposing collection editing.
- [ ] Best-quality autoplay does not begin until preparation completes or the parent explicitly accepts degraded/available playback.
- [ ] Pre-existing local or user-pinned originals are never released or reclassified as slideshow-owned content.
- [ ] Browser memory use stays bounded by lazy resource loading and a small image prefetch window rather than decoding the complete collection.
- [ ] Zero- and one-photo collections remain inside the same protected fullscreen lifecycle and never fail because normal next-image progression is impossible.
- [ ] Focused automated tests cover snapshot stability/order, autoplay state, persisted settings, end behavior, hydration preflight/ownership and navigation guards.
- [ ] Maintainer acceptance is performed on a real phone over the supported remote-access path, including rotation attempts, repeated/random taps and swipes, Back/fullscreen loss, idle/wake behavior, loop/stop/exit behavior and at least one prepared-original slideshow.

## V1 non-goals

- Shuffle/random order.
- Crossfade, Ken Burns or configurable visual transitions.
- Photo counter.
- Metadata, filename, people or Places overlays during playback.
- Editing tags, people, favorites or Places from the slideshow.
- Per-collection slideshow settings.
- Cross-device synchronization of slideshow preferences.
- Automatically supporting unsaved/transient Smart Collection previews.
- Treating the read-only slideshow library as an authentication/authorization boundary; the trusted-LAN application remains unauthenticated.
- Preventing operating-system Home/app-switch/power/notification gestures; use OS app/screen pinning in addition to Protected slideshow when stronger containment is required.
