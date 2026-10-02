---
id: WI-0180
title: Polish slideshow playback preferences controls
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0180: Polish slideshow playback preferences controls

## Context

Issue #488 records usability problems in the shared playback-preferences editor: checkbox controls sit too far from their labels, the checkbox itself is small on phone, the image-duration input is much wider than necessary, and the duration label/value/unit can wrap into an unclear multi-line arrangement.

`SlideshowSettingsEditor` is shared by the slideshow library and in-player settings, so this should be fixed once at the shared component boundary without changing persisted preference semantics.

## Scope

- Give boolean settings a dedicated compact row layout where checkbox and text clearly belong together.
- Keep the whole checkbox label row understandable and tappable, with phone-sized touch targets.
- Increase the effective mobile checkbox/touch target without making desktop controls visually oversized.
- Make `Image duration` use a compact numeric field appropriate to its bounded value range.
- Keep the duration number and `seconds` unit visually together; allow graceful wrapping only at meaningful boundaries.
- Preserve clear spacing and alignment for select-based settings such as Orientation and After last photo.
- Prevent horizontal overflow at supported phone widths.
- Preserve existing settings persistence, normalization and change callbacks.

## Product constraints

This is presentation polish, not a settings redesign. Do not rename or remove existing playback preferences or change their defaults as part of this item.

## Out of scope

- New slideshow preferences.
- Changing autoplay/duration/orientation/end-behavior semantics.
- Replacing native checkboxes with a custom JavaScript control.

## Acceptance criteria

- [ ] Each checkbox is visually adjacent to the text it controls on desktop and phone/PWA.
- [ ] Checkbox rows provide a comfortable touch target on phone and remain keyboard accessible.
- [ ] Image duration renders as a compact control with its value and unit kept together.
- [ ] The duration label does not split above and below the input in a confusing way at narrow widths.
- [ ] The settings editor introduces no horizontal overflow at supported phone widths.
- [ ] The same shared component works correctly from both `/slideshows` playback preferences and the in-player settings panel.
- [x] Existing settings values persist and behave exactly as before; no settings state, normalization or callback code changed.
- [x] Focused Web/markup coverage is present; maintainer desktop and phone verification remains required.

## Implementation notes

PR #495 keeps all behavior in the existing shared `SlideshowSettingsEditor` and changes only its presentation markup and isolated CSS. Boolean preferences now use an explicit `slideshow-settings-check` label row with the checkbox immediately beside its text, a 44 px desktop minimum row height, and a 48 px phone minimum row height with a slightly larger native checkbox.

`Image duration` now groups the compact numeric input and `seconds` inside one nowrap duration-control span. On narrow screens the field label may stack above that grouped control, so the meaningful `value + unit` pair never splits around the input. Orientation and After last photo use the same field grid and selects expand safely to the available phone width.

`SlideshowSettingsEditorStyleContractTests` verifies that both the slideshow library and in-player panel still use the shared component and protects the checkbox-row and grouped-duration layout contract.

## Verification status

Implementation is in review in PR #495. Required CI plus maintainer visual verification on desktop and phone/PWA remain outstanding. Verify both Playback preferences on `/slideshows` and the in-player Settings panel, including checkbox association/tapping, duration layout and horizontal overflow.
