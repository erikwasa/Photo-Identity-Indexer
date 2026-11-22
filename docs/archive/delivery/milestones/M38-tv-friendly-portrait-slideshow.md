---
id: M38
title: TV-friendly portrait slideshow presentation
status_source: ../status/milestones.yaml
depends_on: [M27]
---

# M38: TV-friendly portrait slideshow presentation

## Outcome

Determine whether portrait-heavy slideshows become easier to view on landscape TVs from normal sofa distance, then adopt only solutions supported by real-device evidence. Fully contained 9:16 photos occupy about 31.6% of a 16:9 TV's area. The accepted M27 blurred backdrop (WI-0131) decorates unused space and its 1.03x subtle motion (WI-0130) does not substantially enlarge details.

Experiment with (A) safe subject-focused enlargement, (B) full scene followed by subject detail, and (C) simultaneous full-photo and enlarged-detail panels. Compare each against current Standard. The goal is better perceived face/detail legibility **without unacceptable loss of photographic context**; filling every pixel is not an end in itself.

## Principles

- Start as a reversible opt-in experiment. Existing Standard contain/backdrop remains unchanged and remains default until separately accepted.
- Preserve immutable sources and slideshow snapshots. No original modifications, saved crop derivatives, new catalogue facts, image generation/outpainting, implicit OneDrive hydration or change to Search/Smart/Creative selection.
- Protect all known faces, not just the largest; retain meaningful composition margins. Unknown/unverified geometry, groups, edge subjects, insufficient pixel density and risky crops fall back to contained presentation.
- Distinguish screen orientation from image framing; browser-local TV Focus is not a new global/collection orientation rule.
- Preserve M27 decode-before-show, dual-layer crossfade, bounded memory/prefetch, playback timing, parent controls, navigation, captions, reduced-motion and fullscreen recovery.
- Do not regress WI-0171's prepared-original coarse-pointer phone compositor grid-seam workaround.
- Compare 1600-pixel-class proxies against available originals on 4K screens; apply source-quality-aware zoom limits rather than hidden retrieval/hydration.
- Keep private pixels, file paths, photo identities, per-face positions and raw evaluation materials out of Git; commit privacy-safe aggregate results only.
- Use evidence gates: a failed static enlargement hypothesis is a legitimate no-go and should not force later prototypes.

## Work items

- [WI-0196](../work-items/WI-0196-tv-portrait-benchmark-framing.md) - Define TV portrait-viewing benchmark and safe-framing rules.
- [WI-0197](../work-items/WI-0197-subject-aware-portrait-zoom.md) - Prototype automatic subject-aware portrait enlargement.
- [WI-0198](../work-items/WI-0198-opt-in-tv-focus-playback.md) - Integrate opt-in TV-focus slideshow mode with quality and performance safeguards.
- [WI-0199](../work-items/WI-0199-portrait-context-to-detail.md) - Prototype full-photo then subject-detail sequence.
- [WI-0200](../work-items/WI-0200-portrait-context-detail-layout.md) - Prototype simultaneous context-and-detail layout.
- [WI-0201](../work-items/WI-0201-tv-portrait-acceptance.md) - Compare TV portrait presentation variants and decide adoption.

## Delivery sequence

1. WI-0196: representative fixed sample, TV baseline, viewing-distance protocol and framing safety/quality rules.
2. WI-0197: synthetic-testable deterministic safe crop plan with explicit fallback reasons.
3. WI-0198: opt-in static TV Focus, existing playback guarantee regression coverage and real-TV Gate A. Continue only if actual legibility improves safely.
4. WI-0199 and WI-0200: independent experimental context-preservation variants, only if Gate A supports further investment.
5. WI-0201: comparative maintainer acceptance and independent go/no-go for each approach. Rejected/deferred optional work may be cancelled with explanation.

## Exit criteria

- [ ] Baseline and fixed representative photo/device/distance evaluation protocol are documented.
- [ ] Deterministic crop policy never knowingly clips detected faces and safely reverts for uncertain/unsuitable input.
- [ ] Opt-in TV Focus preserves accepted Standard default, slideshow timing, controls, image quality and mobile-original safety.
- [ ] A physical TV at normal seating distance and phone landscape are used for real comparison, with proxy/original differences evaluated.
- [ ] Candidate full-to-detail and context+detail approaches are compared if Gate A warrants them; otherwise the reason for deferral is recorded.
- [ ] Per-variant evidence includes subject legibility, context retained, comfort, clipping, resource cost and preference, not just area filled.
- [ ] Explicit adoption/rejection/default decisions and any necessary follow-up work are recorded without committing private photo evidence.
