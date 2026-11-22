---
id: WI-0092
title: Add slideshow manual-navigation and orientation preferences
milestone: M22
status_source: ../status/work-items.yaml
depends_on: [WI-0084, WI-0085]
related_adrs: []
affected_modules: [PhotoIdentity.Web, documentation]
---

# WI-0092: Add slideshow manual-navigation and orientation preferences

## Objective

Close two real-phone usability gaps found during M22 maintainer review:

- allow the parent to disable child/manual next/previous navigation while leaving autoplay active; and
- allow the parent to choose the slideshow orientation from slideshow settings instead of always inheriting the orientation present when Start slideshow was pressed.

These are global browser-local slideshow preferences, consistent with the existing M22 settings model.

## Manual-navigation preference

Add a persisted **Manual navigation** setting.

V1 behavior:

- default: **On**, preserving existing tap/swipe/keyboard behavior;
- when On, tap/click advances, swipe left/right navigates, and desktop Left/Right Arrow navigates;
- when Off, presentation-surface tap/click, horizontal swipe and desktop Left/Right Arrow do not change the photo;
- autoplay continues normally when enabled;
- desktop Space and parent Play/Pause continue to control autoplay;
- the two-corner parent unlock, protected Back/fullscreen recovery and deliberate Exit hold remain available;
- disabling navigation must not weaken gesture suppression for image drag, context menu or pinch/zoom where the browser permits suppression.

The setting is about moving between photos, not about disabling all parent interaction.

## Orientation preference

Add a persisted **Orientation** setting with these values:

- **Current at start** — default; preserve the existing behavior by capturing the current orientation when the slideshow starts and locking to that exact orientation where supported, with same-family fallback;
- **Portrait** — request the browser portrait orientation family;
- **Landscape** — request the browser landscape orientation family.

Use the standard Screen Orientation API rather than deprecated vendor-specific APIs.

Changing Orientation from parent settings during an active fullscreen slideshow should apply immediately:

1. release/replace the application-owned orientation lock;
2. request the selected orientation;
3. refresh capability/acquisition status;
4. keep the current slideshow item and playback intent intact.

If the selected lock is unsupported or rejected, keep the slideshow usable and show the existing parent-facing system-rotation-lock guidance. Do not report the requested orientation as active unless the lock actually succeeds.

## Persistence compatibility

An existing settings payload with no new fields should normalize to:

    ManualNavigation = true
    Orientation = current

Do not make an older stored settings object prevent slideshow start.

## Acceptance criteria

- [ ] Manual navigation is available in slideshow settings and defaults to On.
- [ ] With Manual navigation On, tap/click, swipe and Left/Right Arrow preserve current behavior.
- [ ] With Manual navigation Off, tap/click, swipe and Left/Right Arrow never move to another photo.
- [ ] Disabling manual navigation does not stop autoplay, parent Play/Pause, parent unlock, recovery or deliberate Exit.
- [ ] Manual-navigation changes apply to the active slideshow and persist to later slideshows in the same browser profile.
- [ ] Orientation offers Current at start, Portrait and Landscape, with Current at start as the backward-compatible default.
- [ ] Choosing Portrait or Landscape during an active fullscreen slideshow requests that orientation family immediately without changing slideshow membership/position.
- [ ] Current at start retains the existing exact-orientation-first behavior.
- [ ] Orientation acquisition status reflects actual browser success/failure, with the existing fallback warning on failure.
- [ ] Settings tests cover missing, valid and invalid new values.
- [ ] Playback/input tests cover manual-navigation gating without disabling autoplay or parent controls.
- [ ] Real-phone verification covers manual navigation Off and Portrait/Landscape switching where supported.

## Non-goals

- Per-collection input/orientation settings.
- OS-level rotation or kiosk controls.
- Disabling parent Play/Pause.
