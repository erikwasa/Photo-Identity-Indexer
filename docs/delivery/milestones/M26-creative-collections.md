---
id: M26
title: Creative Collections
status_source: ../status/milestones.yaml
depends_on: [M19, M22, M24]
---

# M26: Creative Collections

## Outcome

Photo Identity can turn exact Smart Collection matches into a bounded, story-oriented photo sequence without changing the existing Smart Collection query contract. A Creative Collection starts from explicit anchor matches, groups the surrounding archive into derived moments, expands anchors with relevant context photos and then selects a requested number of diverse photos for presentation.

The milestone is intentionally metadata-first. Capture time is the primary signal for moment grouping; identified people, tags, source/path proximity and location may strengthen a grouping when available, but GPS or named-place metadata is never required. Creative grouping and selection are derived, regenerable presentation decisions rather than canonical facts about the archive.

## Delivery principles

- Keep Smart Collections exact and predictable. Creative Collections consume their results as anchors instead of weakening or redefining filter semantics.
- Separate eligibility, relevance and presentation value. A photo may be useful context even when it does not directly match the anchor filter.
- Treat moments as derived clusters, not canonical Events. Regeneration may change moment boundaries as policy improves.
- Prefer capture-time evidence and work well when location metadata is absent.
- Preserve provenance so the UI can distinguish direct anchor matches from added context.
- Keep selection deterministic for the same catalogue state and policy/version, with stable tie-breaking.
- Reuse the existing immutable slideshow snapshot/playback boundary after creative selection has produced its final ordered revision IDs.
- Do not require semantic-image embeddings for the first milestone. Metadata-only diversity should establish whether the product idea is useful before adding heavier models.
- Do not mutate people, tags, Places, source metadata or original photos while generating a Creative Collection.

## User-visible demonstration

Starting from a saved Smart Collection such as `Alice in 2018`, the operator can preview a Creative Collection that:

1. identifies Alice-matching photos as anchors;
2. includes nearby photos from the same inferred moments even when Alice is not visible in them;
3. labels or summarizes direct matches versus contextual additions;
4. accepts a target photo count; and
5. produces a chronological, diverse slideshow-ready selection rather than every matching photo.

The same workflow must remain useful for archives with little or no GPS/location metadata.

## Work items

- [WI-0118](../work-items/WI-0118-moment-clustering.md) - prototype and evaluate timestamp-first moment clustering over the existing photo catalogue.
- [WI-0119](../work-items/WI-0119-anchor-context-generation.md) - use exact Smart Collection matches as anchors and expand them with provenance-preserving context from inferred moments.
- [WI-0120](../work-items/WI-0120-target-count-diversity-selector.md) - select a requested number of diverse photos from anchor/context candidates and hand the final immutable sequence to slideshow playback.

## Delivery sequence

1. WI-0118 establishes whether useful moment boundaries can be inferred from ordinary family-photo metadata without depending on travel/location coverage.
2. WI-0119 validates the core product idea that a slideshow about a person can include surrounding photographs where that person is not depicted, without silently weakening the original query.
3. WI-0120 addresses collections that are too large or too small by curating toward a requested size while rewarding temporal, moment and people/context diversity and suppressing repetition.

Each stage should remain independently inspectable so unsuccessful heuristics can be replaced without changing canonical Smart Collection or slideshow semantics.

## Exit criteria

- [ ] A documented, versioned moment-clustering policy can group representative family-photo sequences using capture-time-first evidence and can leave uncertain photos ungrouped/singleton rather than forcing a result.
- [ ] Moment clustering is demonstrably useful on a private representative archive sample that includes photos without location metadata.
- [ ] Exact Smart Collection results remain unchanged and can be identified separately as Creative Collection anchors.
- [ ] Context expansion can add photos from the same inferred moments even when selected people are absent, while every added photo records why it was included.
- [ ] A Creative Collection can show direct-match and context counts so the operator can understand what the generator changed.
- [ ] The operator can request a target photo count and receive a best-effort bounded selection without duplicating photos or inventing matches.
- [ ] Selection reduces obvious repetition and improves coverage across time/moments/people compared with simple chronological truncation on a representative private sample.
- [ ] The final ordered revision list can use the existing stable slideshow playback/snapshot lifecycle without changing playback into a live query.
- [ ] Automated tests protect deterministic grouping/selection, provenance, exact-anchor semantics and important edge cases such as zero anchors, fewer candidates than target and many near-consecutive photos.

## Risks

- A single global time-gap threshold may split long activities or merge unrelated adjacent activities. The policy needs explicit uncertainty and evaluation rather than pretending inferred moments are facts.
- Sparse or incorrect capture timestamps can produce poor grouping. Missing metadata must degrade gracefully instead of falling back to misleading import/observation times without evidence.
- Context expansion can overwhelm the subject that motivated the collection. Provenance, bounded expansion and later diversity/target selection must keep anchors meaningful.
- A diversity score can encode arbitrary aesthetic choices and repeatedly suppress personally important photos. Initial rules should be simple, explainable and replaceable.
- Target-count selection may create false precision when the candidate pool is too small or too homogeneous. The UI must treat the count as a best-effort presentation goal rather than a guarantee.
