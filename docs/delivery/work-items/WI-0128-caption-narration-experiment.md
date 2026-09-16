---
id: WI-0128
title: Evaluate local captions and story narration for Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0121]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Web, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
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

- [ ] A bounded local experiment compares deterministic text against at least one practical generative approach if a viable local candidate exists.
- [ ] Generated text is clearly derived/versioned and separated from canonical facts.
- [ ] The evaluation records factual-error behavior, usefulness, runtime and model/package cost.
- [ ] Names, relationships, locations and events are not invented when canonical evidence is absent.
- [ ] The outcome records a go/no-go recommendation and, if positive, a bounded integration path that remains optional during playback.

## Verification requirements

Automated guard/smoke tests for any prototype contract plus maintainer review of representative private outputs, especially factual errors and misleading narrative claims.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
