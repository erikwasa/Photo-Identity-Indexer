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

## Current product decision

Until representative private-archive review and maintained-machine runtime evidence are recorded, the decision is **further experiment / no product influence**. Do not use these experimental reasons to suppress photos, alter Creative Collection ordering, or add a Smart Collection criterion.

If the private review supports a bounded product use, create a separately scoped work item for the semantic change. Persisting technical evidence also requires an explicit storage/versioning design rather than silently treating the experimental JSON shape as canonical archive metadata.
