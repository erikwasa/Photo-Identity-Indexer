---
id: WI-0182
title: Evaluate automatic technical photo quality scoring
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, tools, docs]
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

## Implemented evaluation harness

The evaluation branch adds `proxy-technical-quality-v1`, an OpenCV-backed analyzer over already-generated durable review proxies. The result deliberately has no overall quality percentage. It retains versioned raw evidence for Laplacian sharpness variance, mean luminance, shadow/highlight clipping fractions, luminance p05/p95 and contrast span, proxy dimensions and effective long-edge fraction.

Candidate reason thresholds are applied only to the maintained `1600px / JPEG q78` derivative settings and carry their own calibration version. Other profiles retain raw signals and are marked `profile-not-calibrated`, so resize/JPEG changes cannot silently reuse q78 thresholds.

`PhotoIdentity.PhotoQualityEvaluation` reads a deterministic current-revision sample from PostgreSQL, resolves only catalogue-recorded durable proxies, verifies file length and SHA-256 before analysis, and writes a private JSON review export containing source keys, per-photo evidence, analyzer timing and blank human-review fields. It does not hydrate or read authoritative originals solely for scoring and does not persist quality evidence into the production catalogue.

The private-review procedure and Windows command are documented in `docs/operations/photo-quality-evaluation.md`.

## Candidate-signal disposition before private review

Retain for representative evaluation:

- Laplacian variance as a focus/sharpness candidate signal;
- mean luminance plus extreme shadow/highlight fractions for severe exposure/clipping evidence;
- p05/p95 luminance span for low-contrast evidence;
- proxy dimensions / maximum-long-edge fraction for effective derivative resolution.

Do not retain as a product signal at this stage:

- a combined overall percentage, because weighting would hide materially different failure modes;
- JPEG file size as a quality proxy, because scene complexity and encoder behavior confound it;
- aesthetic/beauty ranking, because it is outside technical-quality scope and has no representative archive evidence.

Noise estimation is deferred from the first calibration because low-light texture, JPEG artifacts, scans and intentional grain make a simple proxy-only noise scalar especially easy to misinterpret. Private review can determine whether it merits a separately versioned experiment.

## Current storage/runtime/product decision

Until private-archive evidence exists, technical-quality evidence remains evaluation output only: no catalogue column/table and no collection semantics. The harness records median/p95 analyzer time and average serialized evidence bytes per photo on the maintained machine so a later decision between on-demand calculation, process-local caching and persistence can use measured cost rather than assumption.

The current product decision is **further experiment / no product influence**. Representative private review is required before recommending display-only evidence, a bounded Creative tie-breaker, or a future collection criterion. Any such semantic change must be a separate work item.

## Acceptance criteria

- The evaluation defines which technical-quality signals are reliable enough to retain and documents rejected/unreliable candidates.
- Representative private-photo review compares automatic evidence with human judgement across common archive failure modes.
- Proxy-profile effects and limitations are documented.
- A versioned, inspectable result shape is proposed if evidence is useful.
- Runtime and storage cost are measured well enough to choose on-demand/cache/persisted handling.
- The evaluation records a concrete product decision: display-only, bounded Creative influence, future collection criterion, further experiment, or no adoption.
- Any follow-up implementation that materially changes Creative selection or Smart Collection semantics is created as separately scoped work rather than silently folded into this evaluation.

## Remaining acceptance evidence

The automated/synthetic layer can establish deterministic metric behavior, profile gating and proxy-only execution, but it cannot establish usefulness on the maintainer's private archive. Before completion, run the documented evaluator against a representative private sample, fill the human-review fields, record maintained-machine runtime/storage measurements, and decide whether the candidate signals are sufficiently reliable for any product use.
