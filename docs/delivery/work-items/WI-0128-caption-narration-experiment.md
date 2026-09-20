---
id: WI-0128
title: Evaluate local captions and story narration for Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0121]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Cli, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Core.Tests, PhotoIdentity.Integration.Tests, models, docs]
---

# WI-0128: Evaluate local captions and story narration for Creative Collections

## Objective

Evaluate whether local image captioning adds enough value to retain, then productize a positive result as optional **photo enrichment** that is independent of Smart Collections and slideshows.

Generated captions belong to the revision-bound derived evidence around a photo, alongside other system-produced information such as extracted metadata, locations and face/identity evidence. Smart Collections, slideshows, the photo-details UI and future search/story features are consumers of that evidence; none of those consumers should cause caption generation.

## Why

Short visual descriptions can make the archive more searchable and understandable and can later enrich collection/story presentation. A caption is useful beyond any one slideshow, so coupling expensive model inference to presentation gives the wrong lifecycle: it makes coverage depend on what the user happens to view, ties processing to a consumer, and makes reuse harder.

The local vision model remains comparatively expensive on the maintainer hardware, so enrichment must be explicitly enabled, background/bounded, local-only and regenerable.

## In scope

- Evaluate local-only captioning on a bounded private sample.
- Separate factual catalogue data from generated prose and never present inferred names/relationships/events as confirmed facts.
- Measure usefulness, factual error/hallucination behavior, runtime and model/package cost.
- Define strict revision/model/prompt/image-mode/context provenance and regeneration semantics.
- Persist guard-passing or guard-blocked caption evidence as derived photo data, never canonical metadata.
- Add a **global server-side caption-enrichment setting**, default off, with Swedish and English generation languages and Swedish as the default.
- When enabled, run one background enrichment worker over eligible current photo revisions that already have durable review proxies and do not yet have evidence for the active generation policy.
- Generate only through a loopback Ollama endpoint using the proven temporary 480x320 thumbnail path.
- Keep presentation/query consumers read-only with respect to caption generation.
- Let the ordinary photo-details surface expose stored caption evidence.
- Let slideshow presentation optionally display already-generated captions without queuing, polling for or generating them.

## Out of scope

- Sending private photos or generated archive descriptions to external LLM/vision APIs.
- Automatically publishing or sharing generated stories.
- Making generated captions canonical photo metadata.
- Generating a caption because a Smart Collection, slideshow or photo page was opened.
- Making slideshow playback wait for model inference.
- Automatic Ollama/model installation.
- Adding caption-text Smart Collection filters or semantic caption search in this correction; the persisted evidence/API establishes the consumer boundary for that later work.

## Acceptance criteria

- [x] A bounded local experiment compares deterministic text against at least one practical generative approach.
- [x] Generated text is clearly derived/versioned and separated from canonical facts.
- [x] The evaluation records factual-error behavior, usefulness, runtime and model/package cost.
- [x] Names, relationships, locations and events are not accepted as generated facts when canonical evidence is absent.
- [x] The outcome records a positive bounded-integration decision.
- [x] Caption enrichment is configured globally/server-side and defaults to off.
- [x] Swedish and English generation are supported, with Swedish as the default.
- [x] Generation is loopback-only, uses the proven 480x320 thumbnail path and runs independently of slideshow/collection activity.
- [x] Current revisions with durable review proxies can be enriched gradually by a single background worker.
- [x] Caption evidence is durably persisted per immutable revision with model digest, prompt version, language, image mode, context and generation runtime.
- [x] Guard-blocked output is retained only as blocked derived evidence and is never exposed as a displayable caption.
- [x] Photo details and slideshow presentation consume persisted caption evidence without triggering generation.
- [ ] Maintainer verification confirms background caption generation progresses with no slideshow/collection open, persisted captions survive restart, and consumers only read the stored evidence.

## Verification requirements

Automated coverage must protect generated-text/prompt contracts, persisted settings defaults, schema/persistence and the consumer/producer boundary.

Maintainer verification should:

1. start Photo Identity with automatic caption enrichment off and confirm no generation occurs;
2. enable **Settings → Automatic photo captions** with Swedish selected;
3. leave the application outside any slideshow/Smart Collection playback and confirm the background worker generates captions for eligible photos;
4. verify a generated caption appears as derived evidence on the ordinary Photo page;
5. restart Photo Identity and confirm the caption remains available without regeneration;
6. open a slideshow with **Show photo captions** enabled and confirm an existing caption can be displayed;
7. use an uncaptained photo in a slideshow and confirm viewing it does **not** enqueue/generate a caption;
8. disable automatic enrichment and confirm existing evidence remains readable while new caption generation stops.

## Implementation status

### Experiment evidence

- The bounded evaluator uses existing durable review proxies and the metadata-first Creative sample only as a convenient representative corpus; the caption contract itself is photo-scoped.
- The practical local candidate is `qwen2.5vl:3b` through an operator-controlled Ollama loopback endpoint. The repository does not bundle the model or Ollama.
- `GeneratedCreativeTextEvidence` records immutable revision, exact model digest, prompt version, generated content and guard flags as regenerable derived evidence.
- The versioned prompt forbids inferred names, relationships, ages, exact locations, dates and event identities. `GeneratedCreativeTextGuard` independently flags those claim classes in English and Swedish.
- The evaluator refuses non-loopback model endpoints.
- Maintainer acceptance on 2026-09-20 found the four-photo thumbnail sample useful 4/4 with 0 observed factual errors and all four outputs passing the guard.
- The four-photo run averaged 95,858.8 ms per caption. A preceding full-proxy single-photo run took 373,568.2 ms and the first thumbnail single-photo probe took 118,081.4 ms.
- The selected `qwen2.5vl:3b` package was 3,200,627,168 bytes.

### Product integration

- Caption enrichment settings are durable catalogue state, not browser-local slideshow state. The default is `enabled=false`, language `sv`.
- PostgreSQL and SQLite persist versioned `photo_generated_captions` evidence tied to immutable asset revisions.
- Guard-blocked generated text is retained internally with its risk flags for diagnosis and future guard tuning, but `DisplayableContent` and the consumer API redact it; Photo Details and slideshows receive blocked status with no caption text.
- Pre-fix blocked rows that stored `content = NULL` are treated as incomplete evidence and become eligible for one automatic regeneration under the same generation policy. Once regenerated text is retained, they are no longer candidates.
- The background worker independently selects current photo revisions that have the configured durable review proxy and lack complete caption evidence for the active language/generation policy.
- Generation is strictly serial and does not depend on what the user views.
- The default production generation policy reuses the accepted probe: `qwen2.5vl:3b`, loopback Ollama, temporary 480x320 JPEG, 1024 context tokens.
- The ordinary Photo page reads and presents the stored caption as derived evidence.
- Slideshow **Show photo captions** is a display preference only. The slideshow performs a read-only lookup for the current revision using the globally configured enrichment language; missing evidence simply means no caption is shown.
- The former slideshow-owned POST/queue/polling caption service introduced in PR #388 is removed by the corrective follow-up.
- Corrective PR #390 owns the archive-enrichment restructuring and CI verification.

## Completion notes

- Trade-off: archive-wide enrichment can take a long time on current hardware. This is acceptable because it is opt-in background enrichment rather than a synchronous user-flow dependency.
- Generated captions remain regenerable model evidence, not archive truth and not a replacement for tags, Places, people or extracted metadata.
- Maintainer production sampling on 2026-09-20 exposed a guard-diagnostics gap: eight blocked Swedish captions were all flagged only as `possible-proper-name-or-location`, while the original blocked text had been discarded. The follow-up retains blocked raw output internally, keeps it non-displayable, and retries only legacy blocked rows whose text is missing so the heuristic can be tuned from evidence rather than guesses.
- Caption generation is intentionally independent of M26 Creative Collection materialization even though Creative Collections were the original experiment consumer.
- Dedicated caption-text/semantic query features can be added later as consumers of the persisted evidence without changing the producer lifecycle.
- Commands/evidence: repository CI plus private maintainer evaluation using `qwen2.5vl:3b`, full-proxy and 480x320 thumbnail probes.
