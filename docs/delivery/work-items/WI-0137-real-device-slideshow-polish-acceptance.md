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

- [x] Maintainer real-device review confirms normal autoplay no longer feels like abrupt hard cuts.
- [x] No reproducible black flash, partially decoded frame or transition-state corruption remains in the representative acceptance set.
- [x] Rapid taps/swipes and loop boundaries remain stable.
- [x] Reduced-motion mode is calm and complete rather than visually broken.
- [x] Motion/backdrop defaults are either accepted, tuned or explicitly disabled with rationale; they are not exposed as settings merely to avoid choosing defaults.
- [x] `/slideshows` supports the intended choose -> tap -> watch path on both true-fullscreen browsers and the documented no-fullscreen fallback path.
- [x] Preparation failures still expose sufficient recovery information without polluting successful starts.
- [x] Browser diagnostics show no unacceptable playback-latency or resource-retention regression from M24.
- [x] M27 completion notes record tested devices/browser versions, representative scenarios and final tuned constants.

## Verification requirements

This item is itself the maintainer acceptance gate. Automated suites must be green before completion, but completion additionally requires documented real-device review.

## Acceptance protocol

Use one stable saved Smart Collection/Creative Collection rather than changing the test content between device passes. The representative set should contain at least 10 distinct photos and, where available, include:

- portrait and landscape images;
- a close-up/single-face photo;
- a two-face photo and a larger group;
- bright and dark images;
- at least one contained image where the adaptive backdrop is clearly visible;
- at least one no-face or uncertain-geometry photo so static fallback can be observed;
- a moment boundary and a retained near-duplicate/burst pair if Creative Collection annotations are present;
- a mix of proxy-served and already-local/original-capable photos where the existing archive naturally provides them.

Do not manufacture catalogue mutations or storage failures just to satisfy this acceptance item. Existing automated preparation-failure coverage remains valid. If an ordinary test collection naturally reaches an actionable preparation failure, verify its recovery surface; otherwise verify that the happy path remains free of routine preparation/status noise.

### Pass A — supported iPhone/browser path

1. Start from `/slideshows` and confirm the normal path is choose collection -> tap -> watch.
2. Let autoplay run through at least 10 distinct photos and one loop boundary.
3. During the same session, exercise tap/swipe navigation, pause/resume and the protected parent unlock flow.
4. Confirm portrait/landscape changes, backdrop treatment, subtle motion/static fallback and adaptive timing remain calm and readable.
5. On the no-standard-Fullscreen-API path, confirm playback starts in the WI-0139 full-viewport fallback, reports reduced browser-level protection to the parent and does not enter an unrecoverable fullscreen pause.
6. Confirm there is no reproducible black flash, partially decoded frame, stale outgoing image or transition-state corruption.

### Pass B — desktop true-fullscreen browser

1. Start the same collection from `/slideshows`; confirm a supported browser still enters true fullscreen.
2. Exercise rapid Next/Previous input for several photos, then resume autoplay.
3. Exit fullscreen unexpectedly and confirm playback pauses on the contained recovery surface; use **Enter fullscreen** to recover.
4. Exercise `Ctrl+Shift+X` protected parent controls.
5. Let the slideshow loop once and confirm no timing/transition state leaks across the loop boundary.
6. Confirm the same mixed-aspect/backdrop/motion choices look intentional at desktop scale.

### Pass C — reduced motion

Enable the operating system/browser reduced-motion preference on either acceptance device, reload the application, and rerun several transitions including one Creative moment boundary if available.

Pass when foreground camera motion is absent, transitions do not depend on animation for correctness, images still appear only after readiness, manual navigation remains stable and the presentation does not look visually broken. Restore the normal preference after the pass.

### Pass D — slower readiness without catalogue changes

On desktop, use browser developer tools to apply a moderate network throttle for the slideshow page, then navigate through several not-yet-presented photos. Do not use this as a throughput benchmark; it is a readiness-state test.

Pass when the current photo remains intact until the incoming photo is ready, configured dwell time is not consumed while the destination image is still loading/decoding, no black/partial frame is exposed, and normal playback resumes when readiness completes.

### Performance comparison

Before the representative phone pass, reset browser/process diagnostics:

~~~powershell
.\measure-slideshow-browser-performance.ps1 -Reset
~~~

After at least 10 distinct displayed photos, capture the report:

~~~powershell
.\measure-slideshow-browser-performance.ps1
~~~

Compare against the accepted WI-0108/M24 phone evidence rather than the old pre-WI-0129 presentation number:

- M24 post-fix baseline: 10/11 explicit prefetch hits after the first image, 12 `collection-viewer-preview-open` operations for 11 displayed images, 13 `api-collection-request` operations, six `original-open` hash reads and no systematic growth with slideshow position.
- Resource Timing baseline averaged about 157 ms. WI-0129 presentation timing now includes the fixed crossfade, so its absolute value is not directly comparable to the old ~7.7 ms visible-event number.
- Investigate rather than automatically reject if average Resource Timing materially exceeds roughly twice the old baseline, repeated multi-second stalls appear, server preview/API work grows to several times the displayed-image count, or presentation time increases systematically with sequence position.
- Passing evidence is bounded, non-growing presentation/resource timing, credible prefetch reuse and no user-visible progressive slowdown or retained-resource buildup during the representative run.

### Candidate final constants

These are the current non-configurable M27 defaults to accept or tune during this item:

| Behavior | Current default |
|---|---|
| Standard crossfade | 600 ms |
| Confirmed moment/chapter crossfade | 850 ms |
| Foreground subtle-motion scale | 1.03x |
| Motion duration | configured image duration, minimum 4 s |
| Motion moving-face limit | at most 2 faces, with conservative geometry limits/static fallback |
| Backdrop | same-resource cover image, 30 px blur, brightness 0.38, saturation 0.72, opacity 0.9, plus 24% black shade |
| Single-face adaptive dwell | 0.98x configured duration |
| Two-face adaptive dwell | 1.04x configured duration |
| Group adaptive dwell | 1.08x configured duration |
| Same visual-group autoplay continuation | 0.55x configured duration, clamped to 1.5–3.0 s |
| Reduced motion | no foreground transform; ready-before-show behavior retained |

Tuning should change these central policies/constants only when the representative real-device evidence shows a repeatable problem. Do not add normal viewer-facing style/timing controls as a substitute for choosing defaults.

### Acceptance evidence to record

Before completing WI-0137, record:

- iPhone model, iOS version and browser/version used;
- desktop operating system and browser/version used;
- representative collection size and covered photo/scenario classes;
- reduced-motion result;
- fullscreen/no-fullscreen/protected-control result;
- slow-readiness result;
- browser diagnostics summary, including sample count, prefetch hits/misses, Resource Timing range/average and whether sequence growth was observed;
- whether the candidate constants above were accepted unchanged or which constants were tuned and why.

## Completion notes

- Files changed: WI-0137/M27 delivery status, work-item and milestone acceptance documentation, generated roadmap/current-work views, and build handoff context. No slideshow runtime code changed during final acceptance.
- Trade-offs: the current M27 defaults were accepted unchanged. Standard/chapter crossfades remain 600/850 ms; subtle motion remains 1.03x with conservative face-safe/static fallback; the accepted backdrop and adaptive dwell/burst timing policies remain as documented. Exact browser build numbers were not recorded, but the tested platforms were iPhone 16e on iOS 27 and Windows with Microsoft Edge.
- Maintainer verification: the representative real-device pass completed successfully on 2026-09-20. Autoplay, manual navigation, looping, protected controls, fullscreen/no-fullscreen recovery, reduced-motion behavior, mixed-aspect presentation, slower-readiness handling and the overall slideshow experience worked as expected.
- Performance evidence: 25 phone presentation samples produced 23 prefetch hits and 2 misses. Visible presentation averaged 617.12 ms and peaked at 677 ms, matching the intentional 600 ms transition envelope rather than showing progressive loading delay. Browser Resource Timing averaged 68.6 ms and peaked at 119 ms, materially below the earlier ~157 ms M24 transfer baseline. The server recorded 26 viewer-preview opens for 25 presentations and no hash reads, with no systematic latency growth by sequence position.
- Diagnostics interpretation: the broader `api-collection-request` count was 81, but that family now includes M27-era collection requests such as slideshow face-geometry and visual-library traffic. Because viewer-preview opens stayed effectively one-per-presentation, prefetch reuse was 92%, hash reads were zero and perceived playback remained responsive, this does not reproduce the pre-WI-0108 request-amplification defect.
- Deferred work: none for M27. Later optional caption work belongs to M26; video remains intentionally deferred under M30.
- Commands/evidence: PR #387 CI run #2013 passed; maintainer ran `.\measure-slideshow-browser-performance.ps1 -Reset`, the representative phone/desktop acceptance flow, and `.\measure-slideshow-browser-performance.ps1` against PostgreSQL schema version 26.
