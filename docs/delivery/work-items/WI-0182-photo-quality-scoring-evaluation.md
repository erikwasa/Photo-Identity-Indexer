---
id: WI-0182
title: Evaluate automatic technical photo quality scoring
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, PhotoIdentity.Integration.Tests, docs]
---

# WI-0182: Evaluate automatic technical photo quality scoring

## Context

Issue #490 asks for some form of automatic image-quality rating. Existing Creative Collection scoring is intentionally a deterministic diversity/presentation policy rather than a quality percentage, and earlier work deliberately avoided introducing opaque aesthetic ranking without representative evidence.

This item is evaluation-first. It should determine whether useful, explainable technical-quality evidence can be derived from existing durable review proxies and how, if at all, that evidence should influence later product behavior.

## Evaluation scope

- Define technical quality separately from subjective aesthetics.
- Prototype measurable evidence such as focus/sharpness, severe under/over-exposure, highlight/shadow clipping, contrast and usable proxy resolution where those signals are reliable.
- Derive evaluation evidence from durable review proxies without hydrating originals solely for quality analysis.
- Account for configured proxy resize/JPEG effects so thresholds are calibrated to the derivative profile rather than presented as measurements of original pixels.
- Produce inspectable subscores/reasons plus an algorithm/version identifier instead of an unexplained single percentage.
- Evaluate representative private archive photos spanning technically good photos, obvious blur, low-light/noisy images, exposure failures, scans and intentionally imperfect but personally important images.
- Compare possible product uses:
  - display-only technical-quality evidence;
  - a bounded Creative Collection preference/tie-breaker;
  - a future Smart Collection criterion;
  - no product use if evidence is too unreliable.
- Measure runtime/storage implications and determine whether process-local caching, persisted derived evidence or on-demand calculation is appropriate.

## Safety and product constraints

Technical imperfection must not be treated as lack of personal value. Do not automatically delete, hide or aggressively suppress photos based only on a quality score.

Do not describe an experimental proxy-derived score as objective photographic quality. Keep technical evidence separate from canonical archive facts, identity evidence and user-authored metadata.

## Out of scope

- Shipping a generic aesthetic-beauty model without representative evaluation.
- Training a new model on the maintainer's private photos.
- Mutating originals or canonical archive metadata.
- Automatic deletion or irreversible exclusion of low-scoring photos.

## Acceptance criteria

- The evaluation defines which technical-quality signals are reliable enough to retain and documents rejected/unreliable candidates.
- Representative private-photo review compares automatic evidence with human judgement across common archive failure modes.
- Proxy-profile effects and limitations are documented.
- A versioned, inspectable result shape is proposed if evidence is useful.
- Runtime and storage cost are measured well enough to choose on-demand/cache/persisted handling.
- The evaluation records a concrete product decision: display-only, bounded Creative influence, future collection criterion, further experiment, or no adoption.
- Any follow-up implementation that materially changes Creative selection or Smart Collection semantics is created as separately scoped work rather than silently folded into this evaluation.
