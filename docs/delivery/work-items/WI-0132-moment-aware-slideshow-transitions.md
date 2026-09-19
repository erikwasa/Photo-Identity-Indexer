---
id: WI-0132
title: Add moment-aware transition language to slideshow playback
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0129, WI-0118]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0132: Add moment-aware transition language to slideshow playback

## Objective

Use derived Creative Collection moment boundaries, when available, to make transitions within one moment feel continuous while giving changes between moments a slightly stronger chapter boundary.

## Why

A slideshow currently presents every adjacent photo with the same semantics. Moment clustering provides structure that can be communicated visually without captions, menus or metadata overlays.

## In scope

- Define a presentation-only moment-boundary annotation that can travel with a slideshow-ready ordered sequence without changing canonical collection/photo facts.
- Use the same base transition renderer for all photos but allow a modest distinction between within-moment and between-moment transitions.
- Consider a slightly longer fade/dip or brief neutral hold at chapter boundaries; keep the treatment restrained.
- Preserve ordinary Smart Collection playback when no moment annotations exist.
- Keep moment grouping/version provenance outside canonical photo metadata.
- Ensure moment semantics affect presentation only, not slideshow membership/order.

## Out of scope

- Creating or editing moment clusters; WI-0118 owns clustering.
- Captions, event names or generated narration.
- Requiring M26 for ordinary M27 slideshow playback.

## Acceptance criteria

- [ ] A slideshow with moment annotations can distinguish within-moment from between-moment transitions without new viewer controls.
- [ ] A slideshow without annotations behaves exactly as the core M27 renderer specifies.
- [ ] Moment annotations cannot add/remove/reorder revision IDs after the immutable sequence is established.
- [ ] Transition differences remain bounded and do not materially extend total playback time unpredictably.
- [ ] Tests cover annotated and unannotated sequences, singleton moments and consecutive moment boundaries.

## Verification requirements

Evaluate on a representative private Creative Collection after WI-0118 produces useful clusters. Record whether viewers can perceive chapter changes without the effect feeling theatrical.

## Completion notes

- Files changed: Creative slideshow snapshot contracts/materialization, slideshow page/presentation renderer, moment transition policy, browser transition interop, JavaScript and integration tests, and delivery status.
- Trade-offs: confirmed moment boundaries use an 850 ms crossfade versus the existing 600 ms crossfade. Same-moment, singleton and unannotated transitions keep the existing behavior; reduced-motion still swaps without animation. No chapter hold, caption or new preference was added.
- Deferred work: maintainer review on a representative private Creative Collection is intentionally grouped with the remaining M27 presentation items before completion.
- Commands run: CI build/test/integration, JavaScript tests and documentation validation are expected on the pull request.
