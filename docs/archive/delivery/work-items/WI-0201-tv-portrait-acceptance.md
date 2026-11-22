---
id: WI-0201
title: Compare TV portrait presentation variants and decide adoption
milestone: M38
status_source: PhotoIdentity.Docs
depends_on: [WI-0198, WI-0199, WI-0200]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0201: Compare TV portrait presentation variants and decide adoption

## Objective

Run a real-world TV/phone evaluation of Standard and viable M38 variants, then explicitly adopt, defer or reject each approach.

## In scope

- Reuse WI-0196 fixed private 30-50-photo sample and normal sofa viewing distance; include single people, pairs, groups, full-body activities, no-face/uncertain geometry, edge cases, low-resolution proxies and prepared originals where available.
- Compare Standard contain/backdrop, static subject-focused TV Focus, full-photo-to-detail and context+detail, alternating/randomizing order or blind-labeling when practical.
- Measure face/detail visibility, photographic context preserved, softness/pixelation, clipping, comfort/distraction, timing, preference and network/GPU/memory cost.
- Retest protected mode, manual/autoplay, pause/resume, loop, fullscreen, captions, reduced motion and WI-0171 coarse-pointer prepared-original artifact conditions.
- Record explicit go/no-go for each variant and any selected opt-in/default policy, fallback thresholds and follow-up issues. A change to existing defaults requires separate acceptance evidence.
- If Gate A rejects static enlargement, document no-go and explicitly cancel/defer unnecessary remaining prototypes rather than completing features for their own sake.

## Out of scope

Publishing private photos or faces, silently changing all users' defaults, image generation/outpainting or catalogue changes.

## Acceptance criteria

- [ ] Maintainer evaluated a physical 16:9 TV at documented normal viewing distance and phone landscape, not only developer monitor previews.
- [ ] Every evaluated variant has a recorded benefit/tradeoff and a specific adopt-opt-in / propose default / rework / reject verdict.
- [ ] No unsafe subject cropping, duplicate exposures, degraded Standard path, phone grid artifacts or unacceptable resource growth remains.
- [ ] Real legibility and retained context both inform decisions; increased screen area alone is not deemed success.
- [ ] Privacy-safe aggregate comparison and future verification/maintenance needs are documented.

## Maintainer verification

1. Record baseline with Standard on the fixed sample at sofa distance.
2. Compare viable variants on the same sample and normal configured dwell; score each independently.
3. Compare proxy vs prepared originals where existing local availability permits; never force hydration.
4. Run longer TV and landscape-phone sessions with pause/loop/manual/reduced-motion checks and diagnostics.
5. Publish aggregate decisions without private images, paths, face coordinates or labels.

## Completion notes

- Files changed:
- Trade-offs:
- Maintainer verification:
- Variant decisions:
- Deferred work:
- Commands run:

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
