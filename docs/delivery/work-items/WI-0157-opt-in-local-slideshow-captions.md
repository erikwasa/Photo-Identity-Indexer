---
id: WI-0157
title: Add opt-in local multilingual slideshow captions
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0128, WI-0084]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Core.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0157: Add opt-in local multilingual slideshow captions

## Objective

Turn the positive WI-0128 local-caption experiment into an optional slideshow enhancement that can gradually generate and reuse safe derived captions without delaying ordinary playback.

## Why

The private WI-0128 review found generated captions useful on all four reviewed photos with no observed factual errors, but local inference remains expensive on the maintainer hardware. Product integration should therefore harvest value incrementally: captions are off by default, generation is bounded and asynchronous, and successful captions are reused rather than regenerated every time a photo appears.

Swedish is the preferred first presentation language for the maintained family archive, while English remains available.

## In scope

- Add a browser-persisted slideshow setting for generated local captions, default off.
- Add a caption-language preference with Swedish and English; default Swedish.
- Generate captions only through a loopback Ollama endpoint and only from the existing durable review proxy, reduced to the proven 480x320 in-memory thumbnail.
- Use a single bounded background worker so slideshow playback never waits for model inference and enabling captions cannot enqueue the entire archive at once.
- Cache generated output as regenerable local derived presentation data keyed by immutable revision, language and generation policy.
- Reuse cached captions immediately on later slideshow views.
- Apply the generated-text guard before display. Unsafe/flagged output is cached as blocked and never shown.
- Keep Ollama and the multi-gigabyte model operator-managed and outside the Photo Identity package.
- Support Swedish guard terms for relationship, event, date, age and possible proper-name/location claims.

## Out of scope

- Making generated text canonical photo metadata.
- Sending photos to remote APIs.
- Blocking a slide while a caption is being generated.
- Bulk captioning the complete archive.
- Semantic photo search or vector persistence. Swedish semantic-search queries remain separate future work from the WI-0127 retrieval result.
- Automatic model installation.

## Acceptance criteria

- [x] Generated captions are an explicit slideshow setting and are disabled by default.
- [x] Swedish and English are selectable caption languages, with Swedish as the default.
- [x] Caption generation is loopback-only, uses the bounded thumbnail path and cannot block slideshow playback.
- [x] Generation is single-worker/bounded and requests only photos actually encountered during caption-enabled playback.
- [x] Guard-passing captions are cached as derived local presentation data and reused; guard-blocked output is never displayed.
- [x] The cache records model digest, prompt version, language, image mode, context and generation runtime without changing canonical catalogue metadata.
- [x] Swedish generated text is covered by the conservative claim guard.
- [ ] Maintainer verification confirms enabling captions produces/reuses Swedish captions in a representative slideshow without disrupting playback.

## Verification requirements

Automated settings/prompt/guard coverage plus maintainer verification with local Ollama running. Verify that an uncached photo continues normal slideshow playback while generation proceeds, that a later view displays the cached caption, and that disabling captions removes the caption surface without deleting the reusable derived cache.

## Completion notes

- Files changed: slideshow settings/editor/presentation, multilingual prompt and guard support, loopback local caption generator, bounded background queue/cache, caption API, tests and operating documentation.
- Trade-offs: current maintainer hardware measured roughly 96 seconds per 480x320 caption with partial CPU/GPU offload, so the integration deliberately favors gradual accumulation and reuse over synchronous generation.
- Deferred work: faster models/hardware tuning and Swedish semantic photo search can be evaluated separately without changing this cache/presentation boundary.
- Commands run: repository CI plus maintainer-run local Ollama verification after merge.
