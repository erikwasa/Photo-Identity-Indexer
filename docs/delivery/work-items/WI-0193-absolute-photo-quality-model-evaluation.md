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

## Checkpoint 1: frozen calibration protocol and WI-0182 baseline

The first implementation checkpoint establishes the reusable evaluation boundary before any learned IQA candidate is selected.

- Candidate score exports are versioned and benchmark-bound. They must cover every benchmark revision exactly once and record candidate/model version, framework, license/source, preprocessing, score direction, model size, execution provider, per-photo runtime and explicit inference failures.
- The WI-0182 baseline is represented as `wi0182-technical-reason-count-v1`: the existing calibrated `proxy-technical-quality-v1` analyzer runs over each durable review proxy and emits a deliberately simple, tie-heavy `100 - 10 * candidate-reason-count` score. This is a fixed comparison baseline, not a production quality model.
- `benchmark quality-eval calibration-report` evaluates only representative photos in the frozen calibration partition with known five-level labels. Challenge photos, `Unsure` labels and all held-out labels are excluded from metrics.
- Calibration metrics currently include Spearman rank correlation, pairwise ordering for human ratings separated by at least two tiers, explicit inference-failure counts and a diagnostic Very Low threshold sweep.
- Very Low threshold evidence reports observed precision, false positives, recall and the 95% Wilson lower bound. Reaching the approximate 98% observed-precision target on a tiny sample does not constitute a safety recommendation.
- A held-out evaluation command is intentionally absent in this checkpoint. It will only be added after candidate/model selection and calibration policy are frozen.

Example baseline and calibration commands:

~~~powershell
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark quality-eval baseline `
  --input private/photo-quality-benchmark.json `
  --output private/wi0182-quality-scores.json

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark quality-eval calibration-report `
  --input private/photo-quality-benchmark.json `
  --scores private/wi0182-quality-scores.json `
  --output private/wi0182-quality-calibration.json
~~~

The next checkpoints add justified learned IQA candidates, candidate-disagreement challenge cases, the bounded proxy-versus-original comparison, then freeze one calibration policy before a single held-out evaluation.

## Completion notes

- Files changed: checkpoint 1 adds the benchmark-bound candidate score protocol, WI-0182 baseline scorer, calibration-only metrics/tests, operator guidance and lifecycle status.
- Trade-offs: the baseline score is intentionally simple and tie-heavy; it preserves WI-0182 as a fixed technical comparison without pretending its hand-written reasons are a calibrated aesthetic score.
- Deferred work: TOPIQ/MUSIQ/ARNIQA-or-equivalent execution, model-disagreement challenge import, original-pixel comparison, frozen calibration mapping and held-out evaluation.
- Commands run: repository CI after pull request creation; maintained private benchmark commands are deferred until the checkpoint merges.

## Checkpoint 2: learned candidate score generation

Added an explicitly opted-in, evaluation-only Python runner for `pyiqa`
TOPIQ NR, MUSIQ, and ARNIQA on frozen `jpeg-1600-q78` benchmark proxies.
It emits the checkpoint-1 versioned score contract and is compatible with
calibration-only reports. A standard-library test suite covers export privacy,
SHA checks, path traversal, inference failures, and no-overwrite semantics.

This provides **implementation plumbing**, not results from the maintained
archive. No model has yet been accepted or calibrated; third-party weight
licenses, pinned model hashes/size, proxy-versus-original evidence and one-shot
held-out results remain required. Documentation includes Windows/PowerShell
setup and commands. Python/PyTorch remain outside the production package.


## Checkpoint 3: calibration-only learned-candidate audit

Added a standard-library Python audit for comparing complete private candidate score exports.
It checks exact benchmark coverage, per-revision score/error validity, candidate identity,
calibration Spearman rank correlation, calibration-only pairwise candidate correlation,
inference failures and runtime/provenance completeness. Held-out/challenge human labels
are explicitly excluded. Unit tests exercise label leakage, missing entries, failures
and candidate disagreement. No maintained-archive model outcomes are claimed.

## Checkpoint 4: maintained private-calibration evidence (2026-10-17)

The maintainer completed benchmark-bound private scoring for **all 580 photos**
with WI-0182 technical baseline, TOPIQ NR, MUSIQ and ARNIQA, with **0 inference
failures** in each export. The review reference includes 238 known-tier
representative calibration ratings (2 calibration Unsure), of which 3 are Very
Low. Public-safe results are in
`docs/operations/photo-quality-model-evidence-2026-10-17.md`; no private
per-photo benchmark/score rows have been committed.

MUSIQ: Spearman 0.370 and >=2-tier ordering 82.8% on calibration. TOPIQ NR:
Spearman 0.359 and >=2-tier ordering 83.8%. ARNIQA: 0.274 / 76.8%.
The WI-0182 technical baseline: 0.234 / 39.6%, with numerous tied scores.
A 1,000-resample paired photo bootstrap shows MUSIQ-minus-TOPIQ Spearman
+0.0111, percentile 95% interval approximately [-0.0546, +0.0806], and
pairwise accuracy -1.01 percentage points, interval [-5.90, +3.68] points.
Both intervals span zero. **No production candidate or mapping is selected.**

The current Very Low diagnostic is **no-go for automatic exclusion**:
TOPIQ's 1/1 correct predicted Very Low has a Wilson 95% lower bound of
20.7% and recall 1/3. Other candidates have no observed threshold meeting
98% calibration precision. Three calibration Very Low labels do not provide
strong evidence about false exclusions.

Next: weight SHA-256/size and third-party license vetting, 20 newly selected
archive-external-to-benchmark disagreement challenges with blinded review,
50-100 photo proxy-versus-original inference comparison, then freeze a
pre-registered candidate/policy **before** one-time held-out analysis.
The benchmark's held-out and challenge human labels are not part of this
calibration analysis and must not be consulted for tuning. No archive or
production behavior changes are authorized by this checkpoint.
