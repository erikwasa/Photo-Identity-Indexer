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

## Completion notes

- Files changed: identity-free slideshow face-geometry API contract/endpoint, deterministic motion policy, dual-layer presentation component/CSS, slideshow playback binding, and focused integration tests.
- Trade-offs: the initial policy used a 1.5% eased zoom around a safe deterministic pivot rather than free panning. This kept the face-safety boundary simple and conservative; photos with more than two faces, edge-near/tight faces, invalid/unavailable geometry, or geometry request failures stayed static.
- Verification finding: on 2026-09-16 the maintainer could not detect any motion on either Android or PC. Code review found the activation path intact; the 1.5% eased transform is below the practical perceptibility threshold for the tested playback durations, so acceptance remains open.
- Follow-up tuning: increase the bounded zoom to 3% and use linear progression so suitable photos have clearly detectable but still restrained movement. The final-transform face-safety check remains authoritative; unsafe cases still render statically.
- Deferred work: repeat Android/PC subjective review after tuning and adjust the fixed constant again only if the effect is still imperceptible or becomes distracting.
- Commands run: repository CI is the authoritative automated validation for the implementation; manual device review remains required after tuning.
