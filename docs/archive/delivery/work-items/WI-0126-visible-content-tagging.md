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

- [x] A reproducible local experiment evaluates useful object/scene/activity concepts on a representative private sample.
- [x] Proxy-versus-original quality and runtime are measured.
- [x] Automatic evidence retains exact pipeline provenance and does not overwrite manual tag history.
- [x] The evaluation compares Creative Collection output with and without semantic tags and records the incremental value.
- [x] The work concludes with a production integration boundary or an explicit decision not to proceed.

## Verification requirements

Automated smoke tests for the experiment/integration contract plus maintainer review of privacy-safe aggregate findings and representative private Creative Collections.

## Implementation status

- The first slice is experiment-only and read-only. It adds a bounded CLIP zero-shot evaluator over existing durable review proxies and does not create automatic catalogue tags.
- The checked-in vocabulary and prompt-template versions make the family-photo concepts inspectable; the evaluator additionally records SHA-256 hashes for the exact local model, tokenizer vocabulary, tokenizer merges and concept vocabulary.
- Semantic diversity is opt-in through the experimental 'm26-visible-content-diversity-v1' selector input. Existing production selector overloads delegate with semantic diversity disabled, preserving prior behavior.
- Review proxies are the default input. Opening source originals requires an explicit bounded '--compare-originals' count and is used only for proxy/original agreement measurement.
- The evaluator emits aggregate concept/runtime/selection evidence and never reports private paths, filenames, collection names or revision ids.
- An explicit '--review-output' option can produce a local-only HTML comparison from copied review proxies so the maintainer can judge whether semantic replacements are actually more useful rather than merely different; this private output is separate from the aggregate report and is not for source control or CI artifacts.
- The representative private run is complete. The current zero-shot controlled-vocabulary approach is a no-go for production automatic-tag evidence and for Creative Collection semantic-diversity scoring.

## Completion notes

- Implementation evidence: PR #374 added the bounded local CLIP experiment; PR #376 added private baseline-versus-semantic visual review output; PR #377 added pinned model/tokenizer acquisition.
- Private evaluation sample: 185 Creative Collection candidates, all 185 scored successfully from durable review proxies, with zero unavailable proxies and zero proxy decode failures.
- Proxy runtime: average 340.8 ms, median 342.0 ms and p95 408.4 ms per photo. The 20-image original comparison averaged 445.3 ms.
- Proxy/original agreement: top-1 agreement was 0.700 and mean top-2 Jaccard overlap was 0.633. This shows the proxy path is operationally viable, but agreement alone is insufficient when the underlying labels are unreliable.
- Selection effect: the semantic policy replaced 8 of 50 baseline selections and increased nominal distinct-concept coverage from 14 to 16.
- Maintainer visual finding: the tested image set contained no birthdays, weddings, babies, dogs or cats, yet the model repeatedly assigned implausible family-event/object labels. Aggregate top concepts included `birthday` for 59 of 185 candidates, `wedding` for 29, `baby` for 9 and `dog` for 1.
- Decision: **no-go** for persisting this zero-shot controlled-vocabulary output as automatic tag evidence and **no-go** for enabling `m26-visible-content-diversity-v1` in production Creative Collections. The nominal diversity gain is not trustworthy because it is partly driven by false semantic labels.
- Production boundary: keep manual tags and the existing metadata/presentation-first Creative selector unchanged. Do not write these experimental labels to the catalogue. Retain the experiment tooling as reproducible evidence and as a possible harness for future model comparisons.
- Scope of conclusion: this rejects the evaluated zero-shot tagging approach, not all future semantic-image work. WI-0127 remains an independent whole-image embedding experiment and should be judged on its own retrieval/diversity evidence.
