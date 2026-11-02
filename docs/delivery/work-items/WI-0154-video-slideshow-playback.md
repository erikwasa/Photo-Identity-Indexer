---
id: WI-0154
title: Integrate video clips into slideshow playback
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0152, WI-0153, WI-0139]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, docs]
---

# WI-0154: Integrate video clips into slideshow playback

## Scheduling decision — 2026-11-02

**Proposed / deferred.** Browser playback (WI-0152) is the next
priority. Mixed-media collection inputs are deferred under WI-0153,
so this workitem now explicitly depends on that collection boundary.
Do not start mixed slideshow work before a supported mixed-media
selection source has been accepted.

## Objective

Allow a user-selected mixture of photos and available playable video
proxies in a slideshow with predictable timing and basic controls.

## In scope when activated

- Explicitly selected mixed photo/video sessions only; leave
  photo-only slideshow defaults and saved photo collections unchanged.
- Reuse existing approved video proxy serving and protected fullscreen,
  wake, navigation and phone/HTTPS paths.
- Use a simple, bounded clip duration/advance rule and a visible
  fallback when a proxy is missing or browser autoplay is blocked.
- No autoplay workaround, automatic hydration, batch transcode or
  mandatory large-load benchmarking.

## Out of scope

Automatic highlights, complex trimming, video recognition, audio
transcription and new media-collection query infrastructure.

## Acceptance criteria

- [ ] An explicitly mixed selection advances photo → video → photo.
- [ ] Clip duration/advance and blocked-autoplay fallbacks are clear.
- [ ] Missing proxy does not hydrate the source or stall the session.
- [ ] Existing photo-only slideshow behavior remains unchanged.

## Verification requirements

Focused transition tests plus a small real-device slideshow check
after WI-0152 and the chosen mixed-media collection contract exist.

## Completion notes

- Status: proposed/deferred; not the immediate next workitem.
