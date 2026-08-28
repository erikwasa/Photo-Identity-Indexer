---
id: WI-0084
title: Build fullscreen Smart Collection slideshow playback
milestone: M22
status_source: ../status/work-items.yaml
depends_on: [WI-0083]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api]
---

# WI-0084: Build fullscreen Smart Collection slideshow playback

## Objective

Build the core read-only slideshow experience on top of the stable WI-0083 snapshot contract: true fullscreen entry, bounded image loading, tap/swipe/keyboard navigation, autoplay and persistent global slideshow preferences.

Toddler-specific lockout and mobile capability hardening are completed by WI-0085; this item establishes the presentation state machine and controls that WI-0085 protects.

## Entry and fullscreen contract

- Add **Start slideshow** to the saved Smart Collection experience.
- Keep the action available for zero- and one-photo collections so they use the same lifecycle as larger collections.
- The click/tap handler must request true browser fullscreen synchronously before awaiting snapshot creation or other network work that could consume the browser user-activation gesture.
- After fullscreen entry, show a neutral preparing surface while WI-0083 snapshot creation completes.
- If fullscreen is denied or lost during initial entry, do not silently continue as if true fullscreen succeeded; show a recoverable slideshow state with an explicit gesture to retry.
- Starting a slideshow creates one navigation/history boundary. Per-photo navigation must not push history entries.

## Presentation contract

- Use a black fullscreen background.
- Center the current image and render it with contain/no-crop fit.
- Do not display a photo-number counter in V1.
- Do not display filename/EXIF/people/Places overlays in V1.
- Keep decoded browser image memory bounded: current image plus a small configurable-in-code previous/next prefetch window, not the entire snapshot.
- Prefer the existing viewer-preview semantics for ordinary playback so an already-local verified original may be used while online-only content can remain proxy-backed without implicit hydration.
- Start the per-image duration only after the destination image has successfully become display-ready.

## Navigation contract

- Tap/click on the normal presentation surface: next photo.
- Swipe left: next photo.
- Swipe right: previous photo.
- Desktop Right Arrow: next photo.
- Desktop Left Arrow: previous photo.
- Desktop Space: play/pause.
- A manual next/previous action resets autoplay timing after the destination image is ready.
- Prevent image dragging/text selection/context menu and browser zoom gestures on the presentation surface where the browser permits it.
- Touch handling must distinguish a horizontal slideshow swipe from the later WI-0085 parent-unlock gesture.

## Autoplay and timer state

- When Autoplay is enabled, playback starts immediately once the snapshot/current image is ready.
- Default image duration is 5 seconds.
- Persist a validated duration supporting at least 1 through 60 seconds.
- Pause freezes the current timer rather than resetting it.
- Resume continues from the frozen timer position.
- A hidden document pauses autoplay and preserves the state needed to resume safely when visible again.
- Loading/preparation time never consumes the configured display duration.
- The optional timer progress bar is non-interactive and appears only while autoplay is active and the setting is enabled.

## End behavior

Persist three choices:

- `loop` — last photo proceeds to first and autoplay continues;
- `stop` — autoplay pauses on the last photo indefinitely;
- `exit` — slideshow exits through the normal deliberate slideshow-exit lifecycle.

Loop is the V1 default.

For one photo, Loop must not visibly reload/flicker the same image each cycle. For zero photos, show `No photos are currently in this collection` and remain in the slideshow shell until deliberate exit.

## Persistent settings

Persist slideshow preferences globally across collections/sessions in the same browser/device profile, using browser-local application storage unless a later requirement introduces cross-device synchronization.

V1 values/defaults:

```text
Autoplay = true
ImageDurationSeconds = 5
ShowTimerProgress = true
AfterLastPhoto = loop
ProtectedSlideshow = true
PrepareOriginals = false
```

WI-0084 owns storage/validation for the whole settings object even though WI-0085 and WI-0086 consume the last two settings.

Corrupt/unknown stored values must fall back safely to documented defaults without preventing slideshow start. Settings changes take effect predictably for the active slideshow and persist for future sessions.

## Acceptance criteria

- [ ] Saved Smart Collections expose Start slideshow for zero, one and many results.
- [ ] Start slideshow calls true fullscreen from the original activation gesture before awaiting snapshot/network operations.
- [ ] A preparing state is displayed while the snapshot/current photo becomes ready.
- [ ] Photos render centered on black with contain/no-crop fit across portrait and landscape source images.
- [ ] Tap/click, horizontal swipe and desktop keyboard navigation follow the documented mapping.
- [ ] Manual navigation does not add browser history entries and resets autoplay timing after image readiness.
- [ ] Autoplay begins immediately when enabled and honors the persisted duration.
- [ ] Pause freezes/resumes timer state rather than resetting it.
- [ ] Hidden-document handling pauses timing and does not skip photos because the tab/app was backgrounded.
- [ ] Timer progress can be hidden and is non-interactive.
- [ ] Loop, Stop and Exit work; Loop is the default.
- [ ] Zero-photo and one-photo snapshots remain valid fullscreen slideshow states.
- [ ] Settings survive slideshow exit/re-entry and apply across different saved Smart Collections in the same browser profile.
- [ ] Invalid persisted settings fall back to safe defaults.
- [ ] Browser image memory stays bounded by a small prefetch window; the implementation does not instantiate/decode an image element for every snapshot item.
- [ ] Normal slideshow playback does not itself request original hydration.
- [ ] Focused tests cover the playback/autoplay state machine, setting persistence/validation, end behavior and zero/one-photo cases.

## Non-goals

- Toddler parent-unlock/history guard/fullscreen-loss hardening beyond the basic recoverable state; WI-0085 owns it.
- Orientation lock and wake lock; WI-0085 owns them.
- Original preparation/hydration; WI-0086 owns it.
- Shuffle, transitions, metadata overlays or editing.
