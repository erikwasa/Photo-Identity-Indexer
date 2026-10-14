# Technical photo-quality evaluation

WI-0182 evaluates explainable **technical** signals from the durable review-proxy layer. It does not introduce an aesthetic ranking, delete/hide photos, or change Creative/Smart Collection selection.

## What the evaluator measures

`proxy-technical-quality-v1` decodes the already-generated review proxy and records inspectable evidence:

- variance of the grayscale Laplacian as a focus/sharpness candidate signal;
- mean luminance;
- fraction of luminance pixels at 0-4 and 251-255 as shadow/highlight clipping evidence;
- 5th and 95th luminance percentiles plus their contrast span;
- actual proxy dimensions and the fraction of the configured maximum long edge that is available.

There is deliberately **no overall photo-quality percentage**. The signals describe proxy pixels, not personal value, composition, subject matter, noise aesthetics, or the technical properties of the original file.

Candidate defect reasons are currently calibrated only for the maintained `1600px / JPEG q78` derivative settings. A profile with different resize/JPEG settings still gets raw measurements, but the result is marked `profile-not-calibrated` and does not apply the candidate defect thresholds. Changing candidate thresholds requires a new calibration/version identifier.

The provisional q78 candidate reasons are intentionally broad enough to find examples for human review:

- `candidate-low-sharpness`
- `candidate-severe-underexposure`
- `candidate-severe-overexposure`
- `candidate-shadow-clipping`
- `candidate-highlight-clipping`
- `candidate-low-contrast`
- `candidate-low-effective-proxy-resolution`

They must not be interpreted as automatic rejection rules.

## Run a private archive evaluation

Use the same catalogue and review-proxy settings as the application. On Windows PowerShell:

```powershell
$env:PhotoIdentity__Postgres__ConnectionString = "Host=127.0.0.1;Port=5432;Database=photoidentity;Username=photoidentity;Password=<your password>"
$env:PhotoIdentity__ReviewProxyRoot = "D:\PhotoIdentity\derivatives"
$env:PhotoIdentity__ReviewProxyProfileId = "jpeg-1600-q78"

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- `
  --output private/photo-quality-evaluation.json `
  --limit 300
```

The tool samples current archive revisions in a stable hash order and only includes revisions that already have the selected durable proxy. It reads and verifies those proxy files; it does not ask for an original, create a source-copy hydration, or mutate catalogue/archive metadata.

The output includes private source keys and review notes. Keep it under `private/`, `data/`, or `staging/` (or an absolute path outside the repository) and do not commit it. Relative output elsewhere is rejected by the tool.

The evaluator reports total elapsed time, median/p95 analyzer time, and average serialized evidence bytes per photo. Use those maintained-machine measurements when deciding whether a future implementation should calculate on demand, cache process-locally, or persist derived evidence.

## Human review protocol

The generated JSON contains a `review` object for every sampled photo. Review a representative subset rather than simply confirming the automatic flags. At minimum include:

- technically good everyday photos;
- obvious blur/focus failures;
- low-light and visibly noisy photos;
- severe under/over-exposure;
- highlight/shadow clipping examples;
- scans or older low-resolution material;
- intentionally imperfect photos that remain personally important.

Fill `review.category`, `review.verdict` (`acceptable`, `technical-failure`, or `uncertain`), `review.personallyImportant`, and `review.notes`. Compare human judgement with each individual signal/reason, paying particular attention to false positives on personally important or intentionally imperfect images and to failures that receive no candidate reason.

## Private-archive result (2026-10-14)

The maintainer reviewed all 300 photos in the deterministic private sample. The private review export is not committed.

Aggregate human verdicts were 249 acceptable, 11 technical failures and 40 uncertain. The candidate analyzer flagged 36 photos: 25 acceptable, 4 technical failures and 7 uncertain. Excluding uncertain cases, the flagged set therefore had 13.8% precision for definite technical failure. The analyzer found 4 of the 11 definite failures (36.4% recall).

Low sharpness was the dominant candidate reason: 29 photos received `candidate-low-sharpness`, but only 3 were definite technical failures; 20 were acceptable and 6 uncertain. Of the seven definite failures with no candidate reason, five were blur/focus failures, one low-resolution and one low-light/noisy. The review included a missed blur/focus failure with very high whole-image Laplacian variance, demonstrating that whole-image sharpness is not equivalent to subject focus.

Three of the eleven definite technical failures were personally important. This is direct evidence for keeping technical-quality evidence separate from personal value.

Runtime was acceptable for further experimentation: 9.63 seconds for 300 proxies, 25.65 ms median analyzer time, 35.25 ms p95, and about 549 serialized evidence bytes per photo.

## Current product decision

The calibrated `proxy-technical-quality-v1` candidate reasons are **not approved for production influence**. Do not use them to suppress photos, alter Creative Collection ordering, add a Smart Collection quality criterion, or generate an authoritative overall quality percentage.

The evaluation result is **further experiment / no adoption of the current candidate rules**. The harness and raw evidence remain useful for benchmarking alternatives.

## Follow-up methods worth evaluating

Reuse the same reviewed 300-photo sample as the first benchmark for alternative estimators:

- **BRISQUE** as a low-cost classical no-reference IQA baseline. OpenCV documents BRISQUE as a blind/referenceless quality estimator whose score ranges from best to worst quality.
- **MUSIQ / TOPIQ / MANIQA** as learned no-reference perceptual-quality estimators. These can provide an overall scalar and are trained to correlate with human quality judgements; TOPIQ is especially interesting because it uses semantic information to focus on locally important distortions.
- **Subject-aware and face-aware scoring**: detect important subjects/faces first and measure focus, blur, exposure and noise on those regions. Face-quality estimators such as MagFace or SER-FIQ can be evaluated for face crops. This directly targets the dominant blur/focus misses from the first experiment.
- **Relative quality ranking for near-duplicates/bursts**: rank similar photos against each other instead of assuming one absolute global quality scale. This is likely to be useful when selecting among multiple photos of the same moment.
- **Personalized pairwise ranking** can be considered later as an opt-in feature, but it should be treated as preference modelling rather than pure technical quality.

A follow-up experiment should benchmark at least BRISQUE, MUSIQ and TOPIQ against the existing human verdicts, add a subject-aware blur/focus experiment, and measure runtime/deployment cost. Any resulting 0-100 presentation score should be a calibrated view over versioned underlying evidence, not an unexplained replacement for it.

If a later evaluation supports a bounded product use, create a separately scoped work item for the semantic change. Persisting technical evidence also requires an explicit storage/versioning design rather than silently treating an experimental result shape as canonical archive metadata.
