---
id: WI-0128
title: Evaluate local captions and story narration for Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0121]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Cli, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Core.Tests, PhotoIdentity.Integration.Tests, models, docs]
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

## Out of scope

- Sending private photos or generated archive descriptions to external LLM/vision APIs.
- Automatically publishing or sharing generated stories.
- Making generated captions canonical photo metadata.
- Requiring narration for ordinary Creative Collection playback.

## Acceptance criteria

- [x] A bounded local experiment compares deterministic text against at least one practical generative approach if a viable local candidate exists.
- [x] Generated text is clearly derived/versioned and separated from canonical facts.
- [x] The evaluation records factual-error behavior, usefulness, runtime and model/package cost.
- [x] Names, relationships, locations and events are not accepted as generated facts when canonical evidence is absent.
- [x] The outcome records a go/no-go recommendation and, if positive, a bounded integration path that remains optional during playback.

## Verification requirements

Automated guard/smoke tests for any prototype contract plus maintainer review of representative private outputs, especially factual errors and misleading narrative claims.

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

## Completion notes

- Files changed: generated-text evidence/guard contract, loopback-only Ollama vision client, bounded narration evaluator, model setup helper, unit/integration tests, CLI help and operator runbook.
- Trade-offs: the experiment intentionally depends on an external local Ollama runtime rather than adding a multi-gigabyte VLM/runtime to the Photo Identity package. That keeps the production package unchanged but makes generation an explicit operator capability. The measured latency makes synchronous caption generation inappropriate on current hardware.
- Decision: go for bounded optional integration through WI-0157. Captions remain presentation-only, generated locally, guarded before display and reusable as regenerable derived cache entries. Ordinary slideshow playback remains independent of the model.
- Deferred work: faster model/hardware tuning and dedicated semantic search remain separate follow-ons.
- Commands run: repository CI plus private maintainer evaluation using `qwen2.5vl:3b`, full-proxy and 480x320 thumbnail probes.
