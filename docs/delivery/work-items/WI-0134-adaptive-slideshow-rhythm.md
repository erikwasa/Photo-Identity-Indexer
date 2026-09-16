---
id: WI-0134
title: Add bounded adaptive timing for a less mechanical slideshow rhythm
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0129]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Web, PhotoIdentity.Web.Tests, docs]
---

# WI-0134: Add bounded adaptive timing for a less mechanical slideshow rhythm

## Objective

Treat the persisted image-duration setting as the viewer's general pace while allowing small deterministic presentation-time adjustments that make autoplay feel less metronomic.

## Why

A rigid five-seconds-per-frame cadence can feel mechanical even after cuts become smooth. Small bounded differences can create a more natural rhythm without asking the user to configure per-photo timing.

## In scope

- Define a simple deterministic timing policy around the configured duration.
- Keep variation narrow enough that a 5-second preference still clearly feels like a 5-second slideshow.
- Allow stable signals such as chapter entry, image complexity proxies already available, group-photo/face count, or presentation annotations to justify small adjustments when evidence is reliable.
- Use the configured duration unchanged as fallback.
- Keep manual-navigation timer semantics predictable: a manually selected destination receives a fresh full policy duration only after it is visible.
- Do not introduce random timing that changes run-to-run.
- Do not add normal UI controls for min/max variation.

## Out of scope

- ML aesthetic scoring.
- Audio beat synchronization.
- Changing the persisted duration preference itself.

## Acceptance criteria

- [ ] The configured duration remains the dominant pace and bounds all automatic adjustments.
- [ ] The same image/context produces the same effective duration under the same policy version.
- [ ] Missing/uncertain presentation evidence falls back to the configured duration.
- [ ] Manual navigation and pause/resume retain understandable timer behavior.
- [ ] Effective timing can be diagnosed/tested without exposing it as normal viewer chrome.
- [ ] Automated tests protect bounds, determinism and fallback semantics.

## Verification requirements

Compare long representative runs with fixed timing and adaptive timing; retain only adjustments that reduce mechanical cadence without making viewers wonder when the next image will arrive.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
