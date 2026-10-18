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

## Deterministic private-archive result (2026-10-14)

The maintainer reviewed the full deterministic 300-photo private sample produced by the evaluation harness. The private JSON remains uncommitted; only aggregate evidence is recorded here.

Human verdicts:

- 249 acceptable;
- 11 technical failures;
- 40 uncertain.

The candidate rules flagged 36 photos and left 264 unflagged. Among the 36 flagged photos, 25 were judged acceptable, 4 technical failures and 7 uncertain. Excluding uncertain judgements, only 4 of 29 definitive flagged cases were technical failures (13.8% precision). Of the 11 definite technical failures in the full sample, 4 were flagged and 7 were missed (36.4% recall).

The most common candidate reason was `candidate-low-sharpness`: 29 photos were flagged, of which 20 were acceptable, 3 technical failures and 6 uncertain. The seven definite failures that received no candidate reason were manually categorized as five blur/focus failures, one low-resolution failure and one low-light/noisy failure. One missed blur/focus failure had very high whole-image Laplacian variance, while multiple acceptable images fell below the current low-sharpness threshold. This shows that whole-image Laplacian variance is not a reliable proxy for whether the important subject is in focus.

Exposure/clipping reasons also proved context-dependent: large dark or bright regions can be intentional or irrelevant to the subject. The low-contrast rule did not fire at all in this sample, so it remains unvalidated. Effective proxy resolution remains objective evidence, but low resolution alone does not establish low personal value.

Three of the eleven definite technical failures were also marked personally important. That reinforces the safety constraint that technical quality must never be treated as permission to delete, hide or aggressively suppress an image.

Maintained-machine cost was small enough for future experimentation: 300 proxies were analyzed in 9.63 seconds, median analyzer time was 25.65 ms/photo, p95 was 35.25 ms/photo, and the average serialized evidence payload was about 549 bytes/photo.

## Product disposition for proxy-technical-quality-v1

The evaluated candidate thresholds are **not suitable for production ranking, filtering, Creative Collection influence, Smart Collection criteria, automatic hiding or deletion**.

The implementation is useful as an evaluation harness and as a source of raw technical evidence, but the calibrated candidate reasons should remain experimental. The main failure is not simply a poorly chosen Laplacian threshold: the private review demonstrates that whole-image sharpness can be high while the intended subject is out of focus, and can be low for otherwise acceptable photographs.

The WI-0182 result is therefore **further experiment / no adoption of the current candidate rules**.

## Alternative quality-rating approaches to evaluate next

The 300 reviewed proxies are useful diagnostic evidence for the failure modes of `proxy-technical-quality-v1`, but the maintainer subsequently judged this deterministic sample insufficiently representative of the archive for selecting a replacement model. M37 therefore creates a new stratified archive benchmark and does not treat these 300 labels as the primary ground truth for future quality-model selection.

1. **Classical no-reference IQA baseline (BRISQUE).** OpenCV exposes BRISQUE as a blind/referenceless image-quality estimator with a scalar score. This is cheap to test and gives a useful non-neural baseline, but it is still a global natural-scene statistic and is not expected to solve subject-focus failures by itself.
2. **Learned no-reference IQA.** Benchmark modern pretrained models such as MUSIQ, TOPIQ and MANIQA. MUSIQ processes multi-scale/native-resolution image representations; TOPIQ explicitly uses high-level semantics to guide attention toward locally important distortions. These models are more promising for an overall perceptual-quality score than hand-written thresholds, but their scores are learned from external opinion-score datasets and must be calibrated against the private archive before product use.
3. **Subject/face-aware technical quality.** Detect important regions first, then evaluate focus, motion blur, noise and exposure locally instead of over the whole image. For photos containing identified faces, face-quality methods such as MagFace or SER-FIQ are candidates for measuring whether a face crop is useful/robust. This directly targets the dominant miss observed in the evaluation.
4. **Relative ranking inside similar-photo groups.** For near-duplicates, bursts or visually similar images, compare which image is technically better rather than forcing a globally meaningful absolute score. Pairwise IQA/ranking models can be evaluated here, and Photo Identity can combine relative quality with existing similarity/identity evidence. Relative ranking is likely to be more actionable for slideshow and collection curation than a universal absolute percentage.
5. **Opt-in personalized ranking as a later experiment.** Pairwise user choices among similar photos could train a lightweight ranker over visual embeddings plus technical features. This could model the maintainer's own notion of a better photograph, but it deliberately mixes technical quality with preference and therefore should remain separate from the first follow-up experiment.

Follow-up evaluation is tracked by M37. It separates absolute per-photo quality from archive-wide visual redundancy, builds a new representative blinded benchmark, evaluates modern no-reference IQA candidates on frozen calibration/held-out partitions, and combines accepted signals only in a later collection simulation. Do not introduce a production 0-100 score, exclusion tier or similarity suppression policy until M37 provides acceptance evidence. If a single quality number is later exposed, retain the underlying model/calibration provenance so it remains inspectable.

## Acceptance criteria

- The evaluation defines which technical-quality signals are reliable enough to retain and documents rejected/unreliable candidates.
- Private-photo review compares automatic evidence with human judgement across common archive failure modes; later model selection requires the broader representative benchmark defined by M37.
- Proxy-profile effects and limitations are documented.
- A versioned, inspectable result shape is proposed if evidence is useful.
- Runtime and storage cost are measured well enough to choose on-demand/cache/persisted handling.
- The evaluation records a concrete product decision: display-only, bounded Creative influence, future collection criterion, further experiment, or no adoption.
- Any follow-up implementation that materially changes Creative selection or Smart Collection semantics is created as separately scoped work rather than silently folded into this evaluation.

## Remaining acceptance evidence

The WI-0182 private run established useful negative evidence about the hand-written candidate rules and maintained-machine cost, but the maintainer later judged its deterministic sample insufficiently representative for selecting a replacement model. M37 carries the broader representative benchmark and successor-model evaluation. WI-0182 should close on its documented no-adoption decision for `proxy-technical-quality-v1`, not by claiming the 300-photo sample is archive-representative.


## Formal WI-0182 closeout — 2026-10-18

**Completed as research, with a clear decision to NOT ADOPT `proxy-technical-quality-v1` automatic defect flags as product selection behavior.** This closes the original evaluation scope while retaining its useful technical evidence harness.

The 300-photo maintainer-reviewed deterministic diagnostics found 4 true technical failures among 29 definitively reviewed flagged photos (13.8% precision) and detected 4 of 11 human-assessed technical failures (36.4% recall). Whole-image Laplacian variance was especially unreliable for subject/face focus; 3 of the 11 failures were personally important. Runtime was inexpensive (25.65 ms median per proxy) but speed does not repair unsafe classification.

The maintainer explicitly judged the 300-photo deterministic set insufficiently representative for accepting a new model. M37 WI-0192 created and accepted a wider 600-photo benchmark (400 representative photos and 200 challenge slots), and WI-0193 compared the WI-0182 reason-count baseline with modern IQA. The WI-0182 baseline had calibration Spearman rho 0.234 and only 943/2379 (39.6%) correct ordering among pairs separated by 2+ human quality tiers. The successor TOPIQ selective score-gap research hypothesis passed the three prespecified *point-estimate* held-out criteria but was **not approved for production**; see WI-0193's consumed one-time held-out report and caveats.

**Operational disposition:** retain `PhotoIdentity.PhotoQualityEvaluation` for versioned, inspectable *offline diagnostics* of sharpness, luminance, clipping, contrast and proxy dimensions; do not expose the original candidate reasons as a quality label, percentage, Creative preference, Smart filter, automatic Very Low exclusion or deletion rule. Existing production behavior remains unchanged. Future subject-aware technical scoring or opt-in preferences require **new bounded workitems**, new independently sourced evidence and model/privacy/licensing checks. The WI-0193 held-out human labels must not be reused to optimize anything.
