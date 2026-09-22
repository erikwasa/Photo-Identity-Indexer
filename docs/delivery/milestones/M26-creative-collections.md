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

WI-0118 through WI-0120 are the core proof-of-value sequence. WI-0121 through WI-0128 plus WI-0158 are deliberately recorded as follow-on product/quality work so useful ideas are not lost while M26 is evaluated. They are not an assertion that every feature must ship: a follow-on may conclude with a measured no-go/retirement decision recorded through the normal work-item lifecycle rather than forcing unnecessary implementation.

## Delivery principles

- Keep Smart Collections exact and predictable. Creative Collections consume their results as anchors instead of weakening or redefining filter semantics.
- Separate eligibility, relevance and presentation value. A photo may be useful context even when it does not directly match the anchor filter.
- Treat moments as derived clusters, not canonical Events. Regeneration may change moment boundaries as policy improves.
- Prefer capture-time evidence and work well when location metadata is absent.
- Preserve provenance so the UI can distinguish direct anchor matches from added context.
- Keep selection deterministic for the same catalogue state and policy/version, with stable tie-breaking.
- Reuse the existing immutable slideshow snapshot/playback boundary after creative selection has produced its final ordered revision IDs.
- Do not require semantic-image embeddings for the first milestone proof. Metadata-only diversity should establish whether the product idea is useful before heavier models are justified.
- Keep presentation preferences/history, derived visual evidence and semantic-model evidence separate from canonical archive/identity facts.
- Measure incremental product value before adding heavier models or vector infrastructure.
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

### Core proof of value

- [WI-0118](../work-items/WI-0118-moment-clustering.md) - prototype and evaluate timestamp-first moment clustering over the existing photo catalogue.
- [WI-0119](../work-items/WI-0119-anchor-context-generation.md) - use exact Smart Collection matches as anchors and expand them with provenance-preserving context from inferred moments.
- [WI-0120](../work-items/WI-0120-target-count-diversity-selector.md) - select a requested number of diverse photos from anchor/context candidates and hand the final immutable sequence to slideshow playback.

### Candidate follow-on product and quality work

- [WI-0121](../work-items/WI-0121-creative-collection-recipes-preview.md) - productize reusable Creative Collection recipes and an inspectable preview before playback.
- [WI-0122](../work-items/WI-0122-photo-presentation-preferences.md) - add explicit photo-level Prefer/Avoid presentation preferences without changing archive truth.
- [WI-0123](../work-items/WI-0123-near-duplicate-burst-groups.md) - detect burst/near-duplicate visual groups so selection can suppress repetitive frames.
- [WI-0124](../work-items/WI-0124-slideshow-history-novelty.md) - track presentation history and optionally favor photos that have not been shown recently.
- [WI-0125](../work-items/WI-0125-person-family-metadata.md) - add optional birth-date and family-relationship metadata for age/relationship-oriented family stories.
- [WI-0158](../work-items/WI-0158-named-creative-collections-slideshow-library.md) - make Creative Collections named first-class objects, allow multiple variants per Smart Collection and expose them from the Slideshows page.

### Candidate follow-on semantic experiments

- [WI-0126](../work-items/WI-0126-visible-content-tagging.md) - re-evaluate local visible-content tagging with Creative Collection quality as the concrete consumer.
- [WI-0127](../work-items/WI-0127-whole-image-embeddings.md) - evaluate whole-image embeddings for semantic retrieval, visual similarity and diversity before adopting vector infrastructure.
- [WI-0128](../work-items/WI-0128-caption-narration-experiment.md) - evaluate optional local captions/story narration only after the simpler Creative Collection product is useful.

## Delivery sequence

1. WI-0118 establishes whether useful moment boundaries can be inferred from ordinary family-photo metadata without depending on travel/location coverage.
2. WI-0119 validates the core product idea that a slideshow about a person can include surrounding photographs where that person is not depicted, without silently weakening the original query.
3. WI-0120 addresses collections that are too large or too small by curating toward a requested size while rewarding temporal, moment and people/context diversity and suppressing repetition.
4. If the core result is useful, WI-0121 is the natural productization step. WI-0122 through WI-0125 are then individually selectable product/quality improvements and should be prioritized from measured everyday value rather than treated as one mandatory block.
5. WI-0126 and WI-0127 are independent semantic experiments. Either may be skipped, rejected or selected based on incremental quality versus runtime/storage complexity.
6. WI-0128 is intentionally late and optional; deterministic titles/templates should remain a valid simpler alternative to generative narration.
7. WI-0158 lifts the deliberately narrow WI-0121 one-recipe-per-Smart-Collection shape into named, independently manageable Creative Collections that can be discovered and launched from the Slideshows page.

Each stage should remain independently inspectable so unsuccessful heuristics/models can be replaced or retired without changing canonical Smart Collection or slideshow semantics.

## Maintainer verification progress

On 2026-09-18, the maintainer accepted the pursued presentation-quality follow-ons:

- WI-0122: Prefer, Avoid and Clear behaved correctly on a representative private Creative Collection; Avoid changed Creative presentation only and the photo remained in the ordinary exact Smart Collection.
- WI-0124: slideshow show counts updated for actually displayed photos; freshness disabled preserved the stable baseline; freshness enabled produced a sensible different selection with more unseen photos.

On 2026-09-18 the maintainer intentionally put WI-0125 on hold.

On 2026-09-19 the maintainer completed WI-0126 with an explicit no-go for the evaluated zero-shot controlled-vocabulary tagging approach. The private run scored all 185 candidates from durable review proxies with no proxy failures; 20 proxy/original comparisons produced 0.700 top-1 agreement and 0.633 mean top-2 Jaccard overlap. Semantic scoring replaced 8 of 50 selected photos and raised nominal concept coverage from 14 to 16, but the visual review found repeated false labels for categories absent from the tested set. Aggregate output assigned `birthday` to 59 candidates and `wedding` to 29 despite neither being present, with additional false baby/dog labels. The result does not justify persisting automatic tags or enabling the semantic-diversity policy in production. Manual tags and the metadata/presentation-first selector remain unchanged. WI-0127 and WI-0128 remain optional independent experiments rather than follow-on commitments.

On 2026-09-20 the maintainer completed WI-0127 with a split decision. The private run embedded all 185 candidates from durable review proxies with zero proxy failures. The 512-dimensional float32 vectors averaged 64.6 ms per proxy and require 2,048 raw bytes each (about 204.8 MB per 100,000 photos before database/index overhead). Semantic text-to-photo retrieval was strong: for the seven queries individually scored in the maintainer note, 7/8 or 8/8 returned photos were relevant. Similar-photo retrieval was inconsistent, with only two of four seeds producing meaningfully related neighbors. Embedding diversity replaced 8 of 50 baseline selections but the maintainer judged the replacements as merely different; mean pairwise cosine changed only from 0.5371 to 0.5354 and p95 from 0.7119 to 0.7008. Therefore M26 will not persist embeddings, add pgvector/ANN infrastructure or enable embedding-based Creative diversity. The semantic-retrieval result is retained as a promising future search direction that should be productized separately if prioritized.

On 2026-09-20 the maintainer accepted the WI-0128 bounded experiment as a positive result. A four-photo 480x320 thumbnail run generated 4/4 captions with zero generation failures; all four passed the guard, all four were judged useful and no factual errors were observed. Average runtime was 95,858.8 ms per caption on the maintainer hardware with the 3,200,627,168-byte qwen2.5vl:3b package. A full-proxy single caption took 373,568.2 ms and the first thumbnail probe took 118,081.4 ms. The quality result justified optional product integration, while the latency ruled out synchronous generation.

WI-0128 productized that positive result as **optional archive enrichment**, not as slideshow-owned processing. Caption generation is globally enabled/disabled in Settings (default off), runs as a single local background worker over eligible current revisions with durable review proxies, and persists versioned caption evidence independently of collections or playback. Photo details, slideshows, Smart Collections and future search/story features are consumers of that evidence; opening a consumer never triggers generation. Swedish and English generation are supported, with Swedish the default.

On 2026-09-22 the maintainer sampled the 30 most recent Swedish `wi-0128-photo-caption-v2` rows. All 30 had empty risk flags, confirming the sentence-boundary proper-name/location correction. The same sample exposed a separate output-quality defect: many model responses ignored the requested single-sentence/20-word shape and several stored outputs ended mid-word or mid-sentence. WI-0128 therefore adds generation policy `wi-0128-photo-caption-v3`, which deterministically normalizes output to one complete sentence of at most 20 words before claim guarding and persistence. Retained v1/v2 text is promoted locally when possible; malformed output that cannot produce a bounded complete sentence remains blocked rather than becoming presentation text. Final maintainer verification later on 2026-09-22 accepted v3: a fresh 30-row Swedish sample stayed within the one-sentence/20-word display contract, the strict SQL validation returned zero displayable violations, blocked relationship evidence remained non-displayable, captions persisted across restart, slideshow consumption remained read-only, and disabling enrichment stopped new work. WI-0128 is therefore complete.

The next explicit Creative Collection product gap is WI-0158: Creative Collections need their own names and durable identities, multiple Creative Collections must be able to share one anchor Smart Collection, and the read-only Slideshows page must make those named Creative Collections directly discoverable and launchable.

With WI-0128 completed and no M26 item currently started, the milestone lifecycle returns to **ready**. WI-0158 is the next ready M26 item; WI-0125 remains intentionally deferred/proposed.

## Exit criteria

- [x] A documented, versioned moment-clustering policy can group representative family-photo sequences using capture-time-first evidence and can leave uncertain photos ungrouped/singleton rather than forcing a result.
- [x] Moment clustering is demonstrably useful on a private representative archive sample that includes photos without location metadata.
- [x] Exact Smart Collection results remain unchanged and can be identified separately as Creative Collection anchors.
- [x] Context expansion can add photos from the same inferred moments even when selected people are absent, while every added photo records why it was included.
- [x] A Creative Collection can show direct-match and context counts so the operator can understand what the generator changed.
- [x] The operator can request a target photo count and receive a best-effort bounded selection without duplicating photos or inventing matches.
- [x] Selection reduces obvious repetition and improves coverage across time/moments/people compared with simple chronological truncation on a representative private sample.
- [x] The final ordered revision list can use the existing stable slideshow playback/snapshot lifecycle without changing playback into a live query.
- [x] Automated tests protect deterministic grouping/selection, provenance, exact-anchor semantics and important edge cases such as zero anchors, fewer candidates than target and many near-consecutive photos.
- [ ] Follow-on work that is pursued retains the canonical-versus-derived/presentation boundaries above; follow-on ideas that are rejected are explicitly closed with evidence instead of being silently abandoned.

## Risks

- A single global time-gap threshold may split long activities or merge unrelated adjacent activities. The policy needs explicit uncertainty and evaluation rather than pretending inferred moments are facts.
- Sparse or incorrect capture timestamps can produce poor grouping. Missing metadata must degrade gracefully instead of falling back to misleading import/observation times without evidence.
- Context expansion can overwhelm the subject that motivated the collection. Provenance, bounded expansion and later diversity/target selection must keep anchors meaningful.
- A diversity score can encode arbitrary aesthetic choices and repeatedly suppress personally important photos. Initial rules should be simple, explainable and replaceable.
- Target-count selection may create false precision when the candidate pool is too small or too homogeneous. The UI must treat the count as a best-effort presentation goal rather than a guarantee.
- Adding many attractive follow-on ideas can obscure whether the core feature itself is useful. Measure WI-0118 through WI-0120 first and prioritize later items from observed shortcomings.
- Semantic models can add storage, packaging and maintenance cost faster than user value. Controlled experiments and explicit no-go outcomes are expected.
