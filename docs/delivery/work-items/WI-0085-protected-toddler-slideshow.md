---
id: WI-0085
title: Harden slideshow playback for toddler-safe phone use
milestone: M22
status_source: ../status/work-items.yaml
depends_on: [WI-0082, WI-0084]
related_adrs: []
affected_modules: [PhotoIdentity.Web, documentation]
---

# WI-0085: Harden slideshow playback for toddler-safe phone use

## Objective

Make the phone slideshow deliberately difficult to leave or reconfigure through accidental child interaction while handling browser fullscreen, orientation and wake-lock limitations honestly.

Protected slideshow is enabled by default and is the primary M22 acceptance mode.

## Protected presentation contract

During protected playback:

- show no ordinary Exit button, settings gear, previous/next buttons or other application chrome;
- ordinary taps/clicks and horizontal swipes can only navigate slideshow content;
- suppress image drag, text selection, context-menu and zoom/pinch behavior on the slideshow surface where supported;
- respect `env(safe-area-inset-*)` and keep parent unlock zones inset from OS gesture/notch areas;
- do not claim to block operating-system Home, app switching, notification shade, power or hardware navigation controls.

Protected mode may be disabled through persisted slideshow settings for normal adult use, but it is On by default.

## Parent unlock contract

Touch devices use two hidden parent zones near the upper-left and upper-right corners, inset inside the safe area.

- Both zones must be held simultaneously for approximately 2 seconds.
- Releasing either zone before the threshold cancels the gesture without side effects.
- Normal taps, one-finger long presses and slideshow swipes must not open parent controls.
- Successful unlock opens a parent overlay with Play/Pause, Settings and Exit slideshow.
- Exit slideshow requires a second press-and-hold confirmation of approximately 1.5 seconds.
- Closing the parent overlay returns to protected playback.

Desktop protected mode uses `Ctrl+Shift+X` to open the same parent controls.

Keep hold durations/zone geometry in a single implementation location so maintainer testing can tune them without changing unrelated slideshow logic.

## Browser history and fullscreen recovery

- Start slideshow creates one normal history boundary; photo changes do not add entries.
- While protected mode is active, Browser Back must not directly navigate to Smart Collections/Photo Details/other application UI.
- Back should pause and enter the protected parent/recovery flow.
- If the browser exits fullscreen unexpectedly, immediately pause autoplay and show a minimal harmless slideshow recovery surface.
- The normal application must not become visible underneath that recovery surface.
- Re-entering fullscreen must be initiated by an explicit user gesture when the browser requires user activation.
- A deliberate parent Exit returns to the originating saved Smart Collection state.

The application cannot prevent browser/OS-level fullscreen escape gestures. The requirement is containment inside a safe slideshow/recovery surface after escape, not impossible-to-exit kiosk mode.

## Orientation contract

On Start slideshow:

1. Capture the current `screen.orientation.type`/orientation family before the user hands over the phone.
2. After fullscreen entry, request an exact orientation lock matching the current orientation where supported.
3. If exact-type locking is rejected but family locking is supported, a portrait/landscape family lock is an acceptable fallback.
4. Never intentionally switch portrait to landscape or landscape to portrait during the active slideshow.
5. Release the application orientation lock on deliberate slideshow exit.

`ScreenOrientation.lock()` has limited browser support and may require fullscreen/mobile context. Capability/acquisition failure is not fatal, but it must produce a parent-facing warning before handoff explaining that the phone's system rotation lock should be enabled. Do not silently display a protected state that did not actually acquire the lock.

## Wake-lock contract

- Request a screen wake lock while slideshow playback/recovery is active where `navigator.wakeLock` is available.
- Keep an owned sentinel/reference and release it on deliberate slideshow exit.
- If the system/browser releases the lock because the document becomes hidden or for power/system policy, listen for the state change and attempt reacquisition when the document is visible again.
- Wake-lock failure is non-fatal but must be surfaced to the parent before handoff.
- The supported WI-0082 secure phone path should provide the secure context required by current browsers for Wake Lock.

## Capability reporting

Centralize browser capability/acquisition status in one JS interop service rather than scattering feature checks through Razor components. At minimum distinguish:

```text
fullscreen: supported / active / failed
orientationLock: supported / active / failed
wakeLock: supported / active / failed
secureContext: true / false
```

The parent overlay/recovery state can use this information for concise diagnostics. Do not show technical diagnostics during normal protected playback unless a required protection failed.

## Toddler-interaction resilience

The implementation should tolerate rapid/repeated taps and swipes without queueing an unbounded number of transitions. Coalesce/serialize navigation so only one image transition is committed at a time and the final state remains valid.

A child holding a finger on the screen, attempting pinch gestures or tapping the progress bar must not open settings/exit or trigger browser-native image actions where browser controls permit suppression.

## Acceptance criteria

- [ ] Protected slideshow is On by default and normal protected playback shows no visible administrative/exit/navigation controls.
- [ ] One-finger taps, long presses and normal slideshow swipes do not open parent controls.
- [ ] Holding both hidden upper-corner zones for the threshold opens the parent overlay on the real target phone.
- [ ] Releasing either corner early cancels parent unlock.
- [ ] Exit requires the second deliberate hold and cannot be triggered by a single normal tap.
- [ ] `Ctrl+Shift+X` opens parent controls on desktop protected playback.
- [ ] Browser Back while protected cannot directly reveal normal Photo Identity application pages.
- [ ] Unexpected fullscreen loss pauses autoplay and leaves only a harmless slideshow recovery surface visible.
- [ ] Fullscreen can be re-entered from an explicit recovery gesture when the browser permits it.
- [ ] Deliberate Exit returns to the originating saved Smart Collection context.
- [ ] The slideshow attempts to lock the exact starting orientation, falling back to the same portrait/landscape family where appropriate.
- [ ] A successful orientation lock prevents portrait/landscape switching during maintainer rotation attempts.
- [ ] Unsupported/failed orientation lock produces a parent-facing system-rotation-lock warning rather than false success.
- [ ] Wake lock is acquired on a supported secure-context phone, reacquired after visibility return when needed and released on deliberate exit.
- [ ] Wake-lock failure/revocation is handled without crashing or silently skipping photos.
- [ ] Capability reporting distinguishes support from successful acquisition.
- [ ] Rapid/repeated navigation gestures do not create an unbounded transition queue or corrupt slideshow position/timer state.
- [ ] Automated tests cover protected-state transitions/history guards and JS interop result handling; real-device acceptance covers multi-touch unlock, Back/fullscreen recovery, rotation and idle/wake behavior.

## Real-device maintainer acceptance

Use the real supported phone path from WI-0082 and exercise toddler-like interaction for a representative autoplay session:

- random/repeated taps;
- rapid left/right swipes;
- long press and attempted pinch/zoom;
- physical device rotation;
- browser Back/fullscreen escape;
- background/foreground transition;
- inactivity long enough to prove screen wake behavior;
- deliberate parent unlock/settings/exit.

Retain only privacy-safe results in repository evidence; do not capture personal photo content in logs/screenshots unless the maintainer explicitly chooses safe test media.

## Non-goals

- OS kiosk/device-owner mode.
- Disabling Home/app-switch/power/notification controls.
- A numeric PIN in V1.
- iOS/Android-specific native application wrappers.
- Guaranteeing orientation lock on browsers that do not expose a working Screen Orientation lock API.
