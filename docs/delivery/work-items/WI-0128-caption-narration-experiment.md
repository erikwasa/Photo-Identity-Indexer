---
id: WI-0128
title: Evaluate local captions and story narration for Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0121]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Cli, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Core.Tests, PhotoIdentity.Integration.Tests, models, docs]
---

# WI-0128: Evaluate local captions and story narration for Creative Collections

## Objective

Evaluate whether local captioning or lightweight story narration adds enough value to Creative Collections to justify generative-model integration.

## Why

Titles, chapter text and short contextual captions could make a slideshow feel more like a story, but generative output adds runtime, model packaging, hallucination and privacy concerns. This should remain an explicit experiment until the simpler Creative Collection experience is proven useful.

## In scope

- Evaluate local-only captioning/narration approaches on a bounded private sample.
- Separate factual catalogue data from generated prose and never present inferred names/relationships/events as confirmed facts unless backed by canonical metadata.
- Compare simple deterministic title/chapter templates against model-generated captions/narration.
- Measure usefulness, factual error/hallucination rate, runtime and packaging cost.
- Define strict provenance/versioning and regeneration semantics for any retained generated text.
- Produce a go/no-go recommendation; a no-go result is acceptable.
- Productize a positive result as an explicit slideshow option that is disabled by default.
- Generate only through a loopback local model endpoint, without delaying ordinary slideshow playback.
- Reuse safe generated captions as regenerable local presentation data instead of regenerating them on every view.
- Support Swedish and English captions, with Swedish as the default presentation language.

## Out of scope

- Sending private photos or generated archive descriptions to external LLM/vision APIs.
- Automatically publishing or sharing generated stories.
- Making generated captions canonical photo metadata.
- Requiring narration for ordinary Creative Collection playback.
- Bulk-captioning the complete archive merely because the feature is enabled.
- Sending captions or photo bytes to a remote model API.

## Acceptance criteria

- [x] A bounded local experiment compares deterministic text against at least one practical generative approach if a viable local candidate exists.
- [x] Generated text is clearly derived/versioned and separated from canonical facts.
- [x] The evaluation records factual-error behavior, usefulness, runtime and model/package cost.
- [x] Names, relationships, locations and events are not accepted as generated facts when canonical evidence is absent.
- [x] The outcome records a go/no-go recommendation and, if positive, a bounded integration path that remains optional during playback.
- [x] Generated captions are exposed as a browser-persisted slideshow setting and are disabled by default.
- [x] Swedish and English are selectable caption languages, with Swedish as the default.
- [x] Caption generation is loopback-only, uses the proven 480x320 thumbnail path, is single-worker/bounded and never blocks slideshow playback.
- [x] Guard-passing captions are cached as regenerable local presentation data and reused; guard-blocked output is never displayed.
- [ ] Maintainer verification confirms enabling captions produces and reuses Swedish captions in a representative slideshow without disrupting playback.

## Verification requirements

Automated guard/smoke tests protect the generated-text contract, multilingual prompt/guard behavior, settings defaults and bounded caption API. Maintainer verification must confirm an uncached slide continues normal playback while generation proceeds, a later view reuses the cached Swedish caption, and disabling captions removes the caption surface without deleting reusable derived data.

## Implementation status

- The bounded evaluator uses the existing metadata-first Creative selection and samples up to 30 selected review proxies; ordinary slideshow playback is unchanged.
- The practical local candidate is `qwen2.5vl:3b` through an operator-controlled Ollama loopback endpoint. The repository does not bundle the model or Ollama.
- `GeneratedCreativeTextEvidence` records immutable revision, exact model digest, prompt version, generated content and guard flags as regenerable derived evidence. The evaluator never persists it.
- The deterministic comparison uses only direct/context provenance, known capture date and identified-person count; names are intentionally omitted.
- The versioned caption prompt forbids inferred names, relationships, ages, exact locations, dates and event identities. `GeneratedCreativeTextGuard` independently flags those claim classes. Flagged text is review-only unsafe output and cannot be treated as accepted factual narration.
- The evaluator refuses non-loopback model endpoints, so proxy bytes cannot be sent to an external host through this command.
- The aggregate report includes model digest/package size, runtime/token counts, guard statistics and failure counts while omitting generated captions and all private identifiers/paths.
- The optional private HTML page shows image, deterministic text, generated caption and guard outcome for qualitative maintainer review.
- Maintainer acceptance on 2026-09-20 found the four-photo thumbnail sample useful 4/4 with 0 observed factual errors and all four outputs passing the guard. The four-photo run averaged 95,858.8 ms per caption on the maintainer machine. A preceding full-proxy single-photo run took 373,568.2 ms and the first thumbnail single-photo probe took 118,081.4 ms. The selected model package was 3,200,627,168 bytes. The result is a positive usefulness decision with a bounded opt-in product path; runtime remains expensive and further tuning is deferred.
- The positive result is productized directly under WI-0128 as **Generated captions (local AI)** in slideshow settings. The setting defaults to off and existing browser settings without the new field remain off.
- Caption language is independently persisted as `sv` or `en`; Swedish is the default.
- An enabled slideshow requests captions only for revisions it actually encounters. A single bounded background worker performs local generation so queue pressure or slow inference cannot hold up slide timing.
- Generation reuses the proven loopback-only `qwen2.5vl:3b` path with a temporary 480x320 in-memory JPEG and 1024-token context. Ollama and the model remain operator-managed and outside the application package.
- Guard-passing output is cached locally by immutable revision, language and generation policy. Cache entries record model digest, prompt version, image mode, context and runtime. Guard-blocked output is cached as blocked and never displayed.
- The conservative claim guard now covers Swedish relationship, event, month/date and age terms as well as English.

## Completion notes

- Files changed: generated-text evidence/guard contract, loopback-only Ollama vision client, bounded narration evaluator, model setup helper, optional Web slideshow settings/presentation, bounded caption API/background worker/cache, unit/integration tests, CLI help and operator/product documentation.
- Trade-offs: the experiment intentionally depends on an external local Ollama runtime rather than adding a multi-gigabyte VLM/runtime to the Photo Identity package. That keeps the production package unchanged but makes generation an explicit operator capability. The measured latency makes synchronous caption generation inappropriate on current hardware.
- Decision: go for bounded optional integration directly under WI-0128. Captions remain presentation-only, off by default, generated locally, guarded before display and reusable as regenerable derived cache entries. Ordinary slideshow playback remains independent of the model.
- Deferred work: faster model/hardware tuning and dedicated semantic search remain separate follow-ons.
- Commands run: repository CI plus private maintainer evaluation using `qwen2.5vl:3b`, full-proxy and 480x320 thumbnail probes.
