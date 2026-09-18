---
id: WI-0137
title: Tune and verify the polished slideshow experience on real devices
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0129, WI-0130, WI-0131, WI-0134, WI-0135, WI-0136, WI-0139]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Web.Tests, docs]
---

# WI-0137: Tune and verify the polished slideshow experience on real devices

## Objective

Evaluate the combined M27 core experience on representative real devices, tune non-configurable defaults and close defects that keep playback from feeling smooth, calm and immediate.

## Why

Transition duration, subtle motion, backdrop strength and timing bounds are perceptual choices that automated tests cannot validate alone. The milestone should end with tested defaults rather than a pile of new settings.

## In scope

- Define a repeatable representative slideshow acceptance set with mixed aspect ratios, close-ups, groups, dark/bright photos, proxies/originals and slower readiness.
- Test on the supported iPhone/browser path and at least one desktop browser, including WI-0139 no-fullscreen fallback where the browser lacks the standard Fullscreen API.
- Verify crossfade/readiness, reduced-motion behavior, subtle motion, backdrop treatment, adaptive timing, library tap-to-play and exception-driven preparation as one flow.
- Exercise autoplay, rapid manual navigation, pause/resume, looping, protected parent controls, fullscreen loss, orientation behavior and wake recovery.
- Record/tune default constants centrally without exposing normal viewer settings.
- Compare browser performance diagnostics to the M24 baseline and investigate regressions.
- Document any effect that is disabled/rejected after real-device evaluation.

## Out of scope

- Shipping every experimental M27 idea regardless of evaluation.
- Moment/burst integrations that remain blocked on M26.
- New preference UI to compensate for poor defaults.

## Acceptance criteria

- [ ] Maintainer real-device review confirms normal autoplay no longer feels like abrupt hard cuts.
- [ ] No reproducible black flash, partially decoded frame or transition-state corruption remains in the representative acceptance set.
- [ ] Rapid taps/swipes and loop boundaries remain stable.
- [ ] Reduced-motion mode is calm and complete rather than visually broken.
- [ ] Motion/backdrop defaults are either accepted, tuned or explicitly disabled with rationale; they are not exposed as settings merely to avoid choosing defaults.
- [ ] `/slideshows` supports the intended choose -> tap -> watch path on both true-fullscreen browsers and the documented no-fullscreen fallback path.
- [ ] Preparation failures still expose sufficient recovery information without polluting successful starts.
- [ ] Browser diagnostics show no unacceptable playback-latency or resource-retention regression from M24.
- [ ] M27 completion notes record tested devices/browser versions, representative scenarios and final tuned constants.

## Verification requirements

This item is itself the maintainer acceptance gate. Automated suites must be green before completion, but completion additionally requires documented real-device review.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
