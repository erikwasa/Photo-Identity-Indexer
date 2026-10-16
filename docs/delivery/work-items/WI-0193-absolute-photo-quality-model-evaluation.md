---
id: WI-0193
title: Evaluate and calibrate absolute per-photo quality models
milestone: M37
status_source: PhotoIdentity.Docs
depends_on: [WI-0192]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Recognition.Onnx, tools, docs]
---

# WI-0193: Evaluate and calibrate absolute per-photo quality models

## Objective

Determine whether Photo Identity can assign a useful absolute quality rating to one immutable photo revision independently of other photos, and whether a conservative Very Low tier can ever be safe enough for normal automatic exclusion.

## In scope

- Compare the WI-0182 `proxy-technical-quality-v1` evidence as a baseline with multiple modern no-reference IQA candidates; initial candidates include TOPIQ, MUSIQ and ARNIQA or justified deployable equivalents.
- Treat third-party model/framework choice as evaluation-only until license, model size, runtime, preprocessing and local deployment implications are measured.
- Map raw model output to an inspectable versioned ordinal/0-100 candidate rating without making the score dependent on archive percentiles.
- Measure ordinal agreement against the WI-0192 blinded five-level labels, rank correlation, confusion by tier and pairwise ordering when human ratings differ clearly.
- Evaluate the Very Low decision separately from general ranking. Prioritize precision over recall, report false exclusions explicitly and report statistical uncertainty/sample size rather than claiming safety from a small count.
- Use an approximate target of 98%+ observed precision for a future normally-excludable Very Low tier; if the held-out evidence cannot support that safely, record no-go/defer even when general ranking is useful.
- Compare review-proxy input with decoded-original input on a bounded representative subset (target 50-100 photos) so resize/JPEG effects are measured before choosing a production source.
- Record per-photo inference runtime aggregate, model footprint, CPU/GPU requirements, input resolution policy and projected one-time backfill cost.
- Keep calibration/model selection confined to the calibration partition and evaluate the chosen policy once on the held-out partition.

## Out of scope

- Persisting production quality evidence.
- Automatically excluding photos.
- Changing Search, Smart Collection or Creative Collection ranking.
- Personalized preference learning or relative-only ranking.

## Acceptance criteria

- [ ] At least the WI-0182 baseline and three justified absolute-quality candidates are measured on the WI-0192 benchmark.
- [ ] One calibration policy is frozen before held-out scoring; held-out results include ordinal/rank behavior and tier confusion.
- [ ] A Very Low exclusion recommendation is made separately and only if false-positive evidence is sufficiently strong; otherwise exclusion is rejected/deferred.
- [ ] Higher human quality is ordered correctly often enough to justify or reject bounded quality preference, including a reported target of roughly 80% pairwise agreement for cases separated by at least two human tiers.
- [ ] Proxy-versus-original comparison determines whether durable review proxies are adequate for production inference or original-pixel transient decode is required.
- [ ] Runtime, model size, preprocessing, licensing/deployment constraints and projected archive backfill cost are documented.
- [ ] The result records a go/no-go candidate model/calibration without changing production semantics.

## Verification requirements

Reproducible benchmark runs, frozen held-out evaluation, maintainer review of representative disagreements and privacy-safe aggregate result documentation.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
