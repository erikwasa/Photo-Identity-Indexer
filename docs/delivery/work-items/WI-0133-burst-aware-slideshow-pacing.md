---
id: WI-0133
title: Add burst-aware compact pacing to slideshow playback
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0129, WI-0123]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0133: Add burst-aware compact pacing to slideshow playback

## Objective

Present retained photos from a detected burst/near-duplicate group as a deliberately compact related sequence instead of giving every highly similar frame the full normal display duration.

## Why

Several near-identical consecutive photos can make a family slideshow feel repetitive. If Creative Collection selection intentionally retains multiple frames, playback can use their relationship to create a short photographic sequence rather than a series of normal-duration duplicates.

## In scope

- Consume derived burst/near-duplicate group annotations from WI-0123 when present.
- Define conservative compact timing for adjacent members of the same group.
- Keep the first/strongest frame readable and avoid turning ordinary sequences into frantic animation.
- Return to the normal configured pace immediately after the group.
- Ensure manual navigation still shows the selected destination deliberately and resets timing correctly.
- Keep burst grouping presentation-only and versioned/derived.

## Out of scope

- Detecting near duplicates or bursts; WI-0123 owns detection.
- Creating GIF/video files.
- Automatically retaining extra burst frames that the collection selector removed.

## Acceptance criteria

- [ ] Adjacent annotated burst members can use a shorter bounded presentation cadence than unrelated photos.
- [ ] Non-burst photos retain normal/adaptive M27 timing.
- [ ] Manual navigation does not trap the viewer in automatic fast-forward behavior.
- [ ] Burst pacing cannot change immutable slideshow membership/order.
- [ ] One- and two-frame groups behave sensibly and very large groups have a bounded policy.
- [ ] Tests cover entry/exit from a burst, manual navigation during a burst and loop boundaries.

## Verification requirements

Review representative private burst sequences and reject any cadence that feels like a glitch rather than an intentional photographic sequence.

## Completion notes

- Files changed: Creative materialization/snapshot contracts, slideshow playback/timing state, slideshow presentation wiring, visual-redundancy integration tests, playback policy tests, and delivery status.
- Trade-offs: the first retained member of a visual-redundancy group keeps normal/adaptive timing. Autoplay-only adjacent continuations use 55% of configured duration, clamped to 1.5-3.0 seconds; manual destinations and loop restarts always use normal/adaptive timing. The accepted WI-0123 visual-group policy is reused rather than recomputed separately for playback.
- Deferred work: maintainer review on representative private burst/near-duplicate sequences is intentionally grouped with WI-0132 and WI-0139 before WI-0137 completion.
- Commands run: CI build/test/integration and documentation validation are expected on the pull request.
