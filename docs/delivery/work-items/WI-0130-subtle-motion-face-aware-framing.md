---
id: WI-0130
title: Add subtle automatic motion and face-aware slideshow framing
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0129]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Web, PhotoIdentity.Web.Tests, docs]
---

# WI-0130: Add subtle automatic motion and face-aware slideshow framing

## Objective

Add restrained, automatic motion to suitable still photos while using existing face geometry to protect important subjects and falling back to a static presentation when a safe move cannot be determined.

## Why

A completely static sequence can feel sterile even with smooth crossfades. Very small deterministic scale/translation can add life, but aggressive Ken Burns effects or unsafe cropping would be distracting and inappropriate for family photos.

## In scope

- Define a conservative deterministic motion policy with small transform bounds.
- Use available face boxes/targets to establish protected framing regions where possible.
- Never knowingly crop a detected face; if safe movement cannot be established, render the foreground image statically.
- Treat group photos, edge-near faces and already tightly framed images conservatively.
- Avoid adding motion intensity/direction settings to the normal slideshow UI.
- Respect `prefers-reduced-motion` by disabling foreground camera motion.
- Ensure transforms compose cleanly with the WI-0129 transition lifecycle and do not restart unexpectedly during pause/resume.
- Add representative tests for motion-policy decisions using synthetic geometry rather than private photos.

## Out of scope

- Face recognition changes.
- Generating new crops or derivative image files.
- Manual pan/zoom controls.
- Per-photo motion authoring.

## Acceptance criteria

- [ ] Suitable photos can receive subtle deterministic motion without user configuration.
- [ ] Detected face rectangles remain inside the visible safe region for every applied transform.
- [ ] Group/tight/uncertain framing cases fall back to static presentation rather than forcing motion.
- [ ] Motion does not interfere with crossfade transitions, pause/resume or manual navigation.
- [ ] Reduced-motion preference disables the foreground motion policy.
- [ ] The policy is deterministic for the same photo geometry and viewport class.
- [ ] Automated tests cover one face, multiple faces, edge-near faces, no-face and unsafe-transform fallback cases.

## Verification requirements

Maintainer review on representative phone playback should compare static versus motion-enabled behavior across portraits, landscapes, close-ups and group photos. Reject/tune any effect that draws attention to itself.

## Maintainer verification

- 2026-09-16: **not accepted**. The maintainer could not detect any foreground motion on either Android or PC while WI-0129 crossfades and WI-0135 library behavior worked as expected.
- Investigation found the motion pipeline and CSS animation are wired, but the shipped baseline was only a 1.5% scale change with `ease-in-out` over the full image duration. The automated tests verified policy decisions, not that motion was perceptible in a real rendered slideshow.
- Geometry-request failures are intentionally converted to a static fallback and previously had no runtime-visible diagnostic, so a production geometry problem could also look identical to intentionally static policy behavior.
- Corrective tuning raises the bounded zoom to 3%, uses linear progression, and adds identity-free `data-photoidentity-motion` diagnostics to distinguish `active`, `paused`, `static-policy`, and `static-geometry-unavailable` states during real-device verification.

## Completion notes

- Files changed: identity-free slideshow face-geometry API contract/endpoint, deterministic motion policy, dual-layer presentation component/CSS, slideshow playback binding, focused integration tests, and corrective runtime motion diagnostics.
- Trade-offs: the first merged policy used a 1.5% scale-only zoom to keep the face-safety boundary simple and conservative, but maintainer testing showed that baseline was too subtle to verify. The corrective baseline uses a still-bounded 3% scale-only zoom; photos with more than two faces, edge-near/tight faces, invalid/unavailable geometry, or geometry request failures still remain static.
- Deferred work: WI-0130 remains open pending repeat Android/PC verification of motion visibility, face-safe fallback, pause/resume and reduced-motion behavior.
- Commands run: repository CI is the authoritative automated validation for the corrective branch; manual device review remains pending after the tuning change.
