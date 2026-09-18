---
id: WI-0126
title: Re-evaluate local visible-content tagging for Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0120, WI-0056]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Cli, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Core.Tests, PhotoIdentity.Recognition.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0126: Re-evaluate local visible-content tagging for Creative Collections

## Objective

Revive the deferred local visible-content tagging experiment with Creative Collection selection and discovery as the concrete consumer.

## Why

People/time metadata can produce useful stories, but whole-photo content such as cake, pets, snow, playgrounds, food or indoor/outdoor context can improve diversity for photos where no selected person is visible. WI-0049 already documented a controlled-vocabulary local-first direction; M26 provides a clearer way to evaluate whether it creates enough product value to justify production integration.

## In scope

- Reuse WI-0049 as prior design evidence rather than assuming its candidate models remain the final choice.
- Evaluate a bounded vocabulary of family-archive concepts using local inference only.
- Compare durable review-proxy inference against originals on the same private sample and prefer proxies when usefulness is materially preserved.
- Keep automatic tag evidence model/pipeline-versioned and separate from manual tag actions.
- Measure whether visible-content signals materially improve Creative Collection diversity/discovery beyond M26 metadata-only selection.
- Define the smallest production integration if the experiment succeeds; explicitly allow a no-go result.

## Out of scope

- External vision APIs receiving private photos.
- Generative captioning/narration.
- Replacing manual canonical tag interventions with opaque model output.

## Acceptance criteria

- [ ] A reproducible local experiment evaluates useful object/scene/activity concepts on a representative private sample.
- [ ] Proxy-versus-original quality and runtime are measured.
- [ ] Automatic evidence retains exact pipeline provenance and does not overwrite manual tag history.
- [ ] The evaluation compares Creative Collection output with and without semantic tags and records the incremental value.
- [ ] The work concludes with a production integration boundary or an explicit decision not to proceed.

## Verification requirements

Automated smoke tests for the experiment/integration contract plus maintainer review of privacy-safe aggregate findings and representative private Creative Collections.

## Implementation status

- The first slice is experiment-only and read-only. It adds a bounded CLIP zero-shot evaluator over existing durable review proxies and does not create automatic catalogue tags.
- The checked-in vocabulary and prompt-template versions make the family-photo concepts inspectable; the evaluator additionally records SHA-256 hashes for the exact local model, tokenizer vocabulary, tokenizer merges and concept vocabulary.
- Semantic diversity is opt-in through the experimental 'm26-visible-content-diversity-v1' selector input. Existing production selector overloads delegate with semantic diversity disabled, preserving prior behavior.
- Review proxies are the default input. Opening source originals requires an explicit bounded '--compare-originals' count and is used only for proxy/original agreement measurement.
- The evaluator emits aggregate concept/runtime/selection evidence and never reports private paths, filenames, collection names or revision ids.
- A representative private run is still required before deciding whether the experiment justifies production automatic-tag evidence.

## Completion notes

- Files changed: Core experimental semantic diversity scoring, local CLIP ONNX/tokenizer adapter, bounded family-photo concept vocabulary, PostgreSQL CLI evaluator, selector/tokenizer/CLI tests, operational evaluation guide and delivery status.
- Trade-offs: no model weights are bundled; automatic evidence is not persisted before usefulness is demonstrated; semantic scoring is capped below established moment diversity and explicit Prefer.
- Deferred work: maintainer private evaluation, proxy/original quality/runtime findings, and a go/no-go production integration decision.
- Commands run: repository CI will provide build/test/documentation evidence; the model-dependent private experiment is intentionally operator-run because model assets and private photos are not part of CI.
