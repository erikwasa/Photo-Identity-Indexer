---
id: M27
title: Slideshow presentation experience
status_source: ../status/milestones.yaml
depends_on: [M22, M24]
---

# M27: Slideshow presentation experience

## Outcome

Photo Identity presents saved slideshows as a smooth, polished, low-friction viewing experience: the viewer chooses a collection and starts watching, while playback handles decoding, transitions, pacing, presentation framing and preparation without exposing routine technical controls.

The milestone deliberately moves complexity into the playback engine rather than into the user interface. Existing browser-local preferences remain authoritative for autoplay, duration, timer visibility, manual navigation, orientation, end behavior, protection and original preparation. M27 should not add a menu of transition, motion or visual-style knobs merely because the renderer becomes more capable.

The core work is independent of Creative Collections. Later moment-aware and burst-aware presentation work may consume M26 outputs when those capabilities are available, but renderer smoothness and library simplification must be useful for ordinary saved Smart Collections on their own.

## Delivery principles

- Prefer automatic presentation policy over new viewer-facing settings.
- Preserve the protected/toddler-safe playback lifecycle and hidden-parent-control model from M22.
- Never charge image loading/decoding time against the configured viewing duration.
- Stage and decode incoming images before they become visible so normal playback does not flash black or expose partially decoded frames.
- Keep browser memory bounded; visual polish must not decode or retain an unbounded slideshow.
- Treat animation as progressive enhancement and respect `prefers-reduced-motion`.
- Keep the foreground photograph legible and conservative. Motion/framing must not crop detected faces or create distracting camera moves; uncertain cases should fall back to static presentation.
- Keep visual decisions deterministic enough to test and tune. Random variation must not make defects irreproducible.
- Preserve explicit recovery when fullscreen, wake/orientation protection, original preparation or image retrieval genuinely needs attention.
- Make the normal consumption path visually driven: choose a slideshow, tap it, watch it.

## User-visible demonstration

From `/slideshows`, a viewer sees photographic collection cards rather than an operator-oriented set of actions. Tapping a collection enters fullscreen and begins playback using the already persisted preferences.

During playback:

1. the next image is prefetched, decoded and staged before presentation;
2. photos transition smoothly without black flashes or abrupt DOM replacement;
3. conservative motion and/or backdrop treatment can add depth without requiring configuration;
4. the configured duration remains the general pace rather than an obviously mechanical cut cadence;
5. moment and burst information, when available, can influence transitions and pacing without changing collection membership; and
6. routine preparation/status details stay out of sight unless the viewer must act.

## Work items

### Core renderer and presentation polish

- [WI-0129](../work-items/WI-0129-dual-layer-slideshow-renderer.md) - replace single-image cuts with a bounded predecoded dual-layer compositor and smooth transitions.
- [WI-0130](../work-items/WI-0130-subtle-motion-face-aware-framing.md) - add conservative automatic motion with face-aware safe framing and reduced-motion fallback.
- [WI-0131](../work-items/WI-0131-adaptive-slideshow-backdrop.md) - evaluate and implement a restrained photographic backdrop for contained images without compromising foreground readability.
- [WI-0134](../work-items/WI-0134-adaptive-slideshow-rhythm.md) - make the persisted image duration a stable general pace while allowing bounded deterministic timing adjustments that reduce robotic cadence.

### Creative-structure presentation integrations

- [WI-0132](../work-items/WI-0132-moment-aware-slideshow-transitions.md) - use derived moment boundaries, when available, to distinguish within-moment transitions from chapter changes.
- [WI-0133](../work-items/WI-0133-burst-aware-slideshow-pacing.md) - present detected burst/near-duplicate groups as compact related sequences instead of repetitive full-duration frames.

These integrations may remain proposed until their M26 dependencies prove useful. They must not block the core M27 renderer/library experience.

### Consumption-flow simplification

- [WI-0135](../work-items/WI-0135-visual-tap-to-play-slideshow-library.md) - redesign the read-only slideshow library around automatic cover images and tap-to-play collection cards.
- [WI-0136](../work-items/WI-0136-exception-driven-slideshow-preparation.md) - hide routine original-preparation mechanics from normal consumption and surface only states that require action.
- [WI-0139](../work-items/WI-0139-no-fullscreen-slideshow-fallback.md) - allow immersive slideshow playback when the standard Fullscreen API is unavailable, while keeping reduced-protection status explicit.
- [WI-0137](../work-items/WI-0137-real-device-slideshow-polish-acceptance.md) - run real-device acceptance, tune presentation defaults and verify the combined experience remains smooth, bounded and toddler-safe.

## Delivery sequence

1. WI-0129 establishes the rendering foundation: two bounded presentation layers, explicit decode readiness and transition lifecycle.
2. WI-0130 and WI-0131 independently explore depth/motion and backdrop treatment on top of that renderer. Either must degrade cleanly to the simpler renderer when unsupported or visually unsafe.
3. WI-0134 tunes timing so autoplay feels less mechanical without adding another preference surface.
4. WI-0135 and WI-0136 simplify the entry path so routine use becomes choose -> tap -> watch, while parent/recovery controls remain available when genuinely needed.
5. WI-0132 and WI-0133 integrate Creative Collection structure only after the relevant M26 evidence exists; they are presentation consumers, not reasons to alter canonical collection semantics.
6. WI-0139 closes the no-Fullscreen-API startup dead end before final acceptance.
7. WI-0137 performs representative phone/browser acceptance and records the defaults that should ship.

## Exit criteria

- [x] Ordinary slideshow advancement does not visibly flash black, show partially decoded content or expose abrupt single-element replacement during successful playback.
- [x] The incoming photo is staged and decode-ready before the transition begins, with loading time excluded from configured display duration.
- [x] Transition/motion work keeps browser memory bounded and does not regress the M24 slideshow latency diagnostics on representative phone playback.
- [x] `prefers-reduced-motion` produces a calm presentation without unnecessary transforms while preserving seamless image replacement.
- [x] Automatic motion/framing never knowingly crops detected faces; uncertain or unsuitable photos fall back to static presentation.
- [x] Contained portrait/landscape photos have a deliberate presentation treatment that avoids a sterile empty-screen feel without obscuring the source photo.
- [x] The persisted image duration remains the user's pace preference; any automatic variation is bounded, deterministic and does not become another normal setting.
- [x] `/slideshows` is visually collection-first, uses automatic representative covers and supports starting playback by activating the collection card itself.
- [x] Routine original preparation/status UI is absent from the normal happy path; actionable failures and recovery remain explicit and parent-safe.
- [x] Moment/burst presentation integrations, when enabled, change pacing/transition language only and do not silently change slideshow membership.
- [x] Browsers without the standard Fullscreen API can still start an immersive full-viewport slideshow, with reduced browser-level protection reported explicitly rather than entering an unrecoverable pause loop.
- [x] Real-device acceptance covers autoplay, manual navigation, protected controls, orientation/fullscreen recovery, no-fullscreen fallback, reduced motion, mixed aspect ratios, slow image readiness and repeated loops.
- [x] Automated tests cover transition lifecycle, timer/reset semantics, bounded staging, fallback behavior and the simplified library/startup states.

## Closeout evidence

M27 was accepted on 2026-09-20 after the final WI-0137 representative real-device pass.

- **Phone:** iPhone 16e on iOS 27 using the supported iPhone browser path; exact browser build was not recorded.
- **Desktop:** Windows with Microsoft Edge; exact Edge build was not recorded.
- The combined pass covered autoplay, rapid/manual navigation, pause/resume, looping, protected parent controls, desktop fullscreen loss/recovery, the no-standard-Fullscreen-API phone fallback, reduced motion, mixed aspect ratios and deliberately slower image readiness.
- The maintainer reported the combined slideshow experience worked as expected with no reproducible black flash, partial-frame exposure, transition corruption or progressive slowdown.
- Phone browser diagnostics captured 25 presentation samples: 23 prefetch hits and 2 misses, 617.12 ms average visible-presentation time (677 ms max), 68.6 ms average Resource Timing (119 ms max), 26 viewer-preview opens and zero hash reads. Timing remained bounded rather than growing with slideshow position.
- The ~617 ms presentation average is consistent with the accepted 600 ms standard crossfade because WI-0129 measures through final visible-layer completion. The lower 68.6 ms Resource Timing compares favorably with the earlier ~157 ms M24 transfer baseline.
- The broader 81-count `api-collection-request` family is not treated as a direct regression against the old WI-0108 count because M27 now adds collection-family requests such as per-photo slideshow face geometry and visual-library traffic. The image-serving-specific preview count remained effectively one per displayed image and prefetch reuse remained high.
- All M27 non-configurable presentation defaults were accepted unchanged; no viewer-facing transition/motion/backdrop controls were added.

## Risks

- Crossfades can mask slow loading without actually fixing it. Decode readiness and diagnostics must remain explicit rather than relying on animation to hide stalls.
- Two visible layers plus backdrops can increase GPU/memory pressure on older phones. The design must remain bounded and use conservative effects.
- Motion can feel artificial or crop important content. Face-aware safe areas, small transforms and static fallback are more important than making every photo move.
- Blurred photographic backdrops can look cheap or distract from the foreground. WI-0131 should retain black/neutral fallback and be judged on representative family photos.
- Adaptive timing can become unpredictable. Variation should be small and explainable, with the configured duration clearly remaining the governing pace.
- Moment/burst integration depends on M26 outputs that are still experimental. Core M27 value must not depend on those items shipping.
- Simplifying the library must not remove recovery paths needed for storage, fullscreen, orientation or protection failures; complexity should be hidden on success, not deleted.
