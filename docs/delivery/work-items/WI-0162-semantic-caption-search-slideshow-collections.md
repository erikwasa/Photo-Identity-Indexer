---
id: WI-0162
title: Scale semantic photo search and create slideshow collections from caption search
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0127, WI-0128, WI-0145]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Worker, PhotoIdentity.Integration.Tests, docs]
---

# WI-0162: Scale semantic photo search and create slideshow collections from caption search

## Objective

Productize the successful text-to-photo retrieval signal from WI-0127 at a much larger evaluation scale, combine it with the durable Ollama captions from WI-0128, and let the operator turn search results into named explicit slideshow collections.

## Why

WI-0127 produced a clear split result: CLIP-style text-to-photo retrieval was strong on the evaluated natural-language queries, while similar-photo retrieval and embedding-based Creative Collection diversity did not justify production adoption. WI-0128 later provided higher-detail local captions but at high generation cost. These two signals are complementary: fast semantic visual retrieval can cover photos before expensive caption enrichment is complete, while generated captions can contribute richer searchable text where available.

The next experiment should therefore scale the part that worked instead of reviving the rejected vector-diversity path.

## In scope

- Re-run text-to-photo semantic retrieval on a substantially larger representative archive sample than WI-0127 and with a substantially larger query suite covering people-free visual concepts, activities, objects, scenes, seasons/weather, indoor/outdoor contexts and ordinary family-photo language.
- Record query-level relevance evidence rather than relying only on aggregate similarity scores.
- Measure indexing/generation runtime, storage cost and query latency at the larger scale.
- Evaluate practical local persistence/indexing approaches only as needed for search; do not assume pgvector/ANN adoption before measurement.
- Expose one natural-language photo-search surface that can draw evidence from:
  - semantic image/text similarity from the successful WI-0127 direction; and
  - durable displayable generated captions from WI-0128.
- Preserve provenance so a result can explain whether it matched semantic visual evidence, generated caption text, or both.
- Make useful search possible even when a photo has no generated Ollama caption yet.
- Support Swedish search input as an explicit evaluation/product requirement, including measurement of whether query translation, multilingual text encoding or another local strategy is needed for the semantic path.
- Allow the operator to select/search a result set and save it as a named explicit photo-list/slideshow collection using the WI-0145 persistence model.
- Preserve the saved collection as an immutable ordered revision list at save time; later model/caption changes must not silently rewrite the saved slideshow.
- Keep semantic evidence derived/versioned and separate from canonical photo metadata, tags, people and Places.

## Out of scope

- Reviving WI-0127 embedding-based Creative Collection diversity without new evidence.
- Treating nearest-neighbor similarity as canonical tags or captions.
- Requiring Ollama captions to exist before a photo is searchable.
- Generating captions synchronously when search is opened.
- Automatically mutating existing saved slideshow collections as search models evolve.

## Acceptance criteria

- [ ] The larger evaluation uses materially more photos and materially more text queries than WI-0127 and records per-query relevance evidence.
- [ ] The evaluation separately reports semantic-only, generated-caption-only and combined retrieval quality where comparable evidence exists.
- [ ] Search remains useful for photos without generated Ollama captions.
- [ ] Swedish natural-language search is evaluated explicitly and the chosen local strategy is documented with measured quality/latency.
- [ ] Similar-photo and embedding-diversity features remain disabled unless new measured evidence independently justifies reopening them.
- [ ] The product search surface indicates or retains provenance for semantic versus caption matches.
- [ ] Search results can be saved as a named explicit slideshow/photo-list collection and launched through normal slideshow playback.
- [ ] A saved result collection retains its revision membership even if captions or semantic model evidence later change.
- [ ] Automated tests protect result provenance, caption-absent behavior, collection materialization and immutable saved membership.

## Verification requirements

Use a representative private archive sample large enough to expose scaling behavior and a broad query suite rather than the small WI-0127 probe set. Report query-level relevance, latency and storage evidence, then perform maintainer review of mixed semantic/caption searches in both Swedish and English. Save at least two result sets as named slideshow collections, restart the application, and verify both remain independently launchable with unchanged membership.

## Design notes

- The term "caption" in the UI may cover two different evidence sources, but storage/provenance must not conflate them. WI-0128 text is generated natural-language evidence; CLIP-style retrieval is vector similarity evidence and should remain identifiable as such.
- The explicit photo-list collection model is the correct save boundary for arbitrary semantic search results because saving freezes the current result membership without redefining exact Smart Collection semantics.
- If large-scale measurement shows that persisting image embeddings is justified, the storage/index choice should be documented as an explicit decision with model/version provenance, rebuild behavior and size estimates. The earlier WI-0127 no-go against adopting pgvector merely for diversity remains valid until superseded by search-specific evidence.
