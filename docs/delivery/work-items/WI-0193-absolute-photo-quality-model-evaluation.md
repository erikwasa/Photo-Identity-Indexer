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

## Checkpoint 5: non-benchmark TOPIQ/MUSIQ disagreement challenges

Added an explicit `--pool-only` mode to the existing catalogue-driven
challenge generator. It selects a bounded technical/rarity-diverse screening
pool of **new** current photos outside the 580-photo benchmark, without using
the remaining 20 reserved challenge slots. The generator excludes benchmark
revisions/content hashes and deduplicates the pool by original content SHA.

The Python `disagreement_challenges.py` command scores the new candidate
pool separately with TOPIQ NR and MUSIQ, using the same verified immutable
review proxies and in-memory manifest. It rejects incomplete, nonfinite or
incorrectly bound results. The private 20-photo selection uses rank
disagreement *within that unlabeled pool*, not any representative calibration,
held-out or challenge human rating. The output matches the existing private
`benchmark add-challenges` interchange format and clears all human labels.
Standard-library offline tests cover overlap, duplicate content, blind
selection, complete score validation, SHA-checked inference and reserved-slot
limits. See `docs/operations/photo-quality-benchmark.md` for commands.

This checkpoint has **not** run the maintained-archive screening/scoring; no
new challenge photos are claimed or reviewed. The current 580-photo candidate
exports will require an explicit extension/reconciliation after importing
20 new photos, so import may reasonably wait until that tooling is in place.
The technical/rarity-diverse pool is a bounded sample, not a claim to the
largest model disagreements in all 27k archive photos.

Outstanding acceptance gates remain model-weight/license provenance,
50–100 original-versus-proxy pairs, model/ordinal calibration freeze and
one-shot representative held-out evaluation. No production ranking or
automatic Very Low exclusion is added.

## Checkpoint 6: strict reconciliation after importing 20 disagreement challenges

Added `reconcile_quality_scores.py`, a private, offline reconciliation
utility that validates the explicit **pre-import 580-photo benchmark snapshot**,
the **post-import 600-photo benchmark** and the exact **20-photo selected
challenge file**. It rejects changed frozen core pixels/metadata/ordering,
mismatched benchmark IDs, altered representative partition counts, content
overlap, unexpected challenge provenance and incomplete/duplicate candidate
score exports.

For TOPIQ NR and MUSIQ, the script extracts only the selected 20 score rows
from the previously scored non-benchmark screening pool and combines them
with the original full 580. For ARNIQA, it offers a verified selected-only
inference runner over just the 20 new proxies. WI-0182 remains fast enough to
rerun over all 600. This avoids paying for another 580-photo learned-model
pass while preserving the complete score coverage required by the existing
calibration tools. Supplemental candidate identity/direction/device/version
metadata must match the original score export exactly.

The original 580-photo benchmark snapshot and four score exports are
preserved, not overwritten. The resulting new 600-photo score exports have
a reconciliation protocol and explicitly identify `totalElapsedMilliseconds`
as the sum of per-image runtimes rather than end-to-end wall time. Neither
the new tool nor its synthetic tests reads held-out or challenge human
labels for calibration/selection. The existing blinded reviewer handles
diagnostic labels for imported cases. See the updated operational runbook.

**Maintainer execution still pending:** generate/score the distinct pool,
select 20, explicitly back up the benchmark before importing, import 20,
extend three learned score exports, rerun baseline and inspect aggregate
audit results. Weight-license provenance and proxy/original assessment are
still outstanding; no model is selected and automatic exclusion remains
unsupported.

## Checkpoint 7: completed private blinded review and original-pixel sensitivity tooling

On 2026-10-17 the maintainer completed the blind benchmark review after
adding the 20 reserved non-benchmark TOPIQ/MUSIQ disagreement challenges.
The supplied **private** version-2 manifest confirms the 600-photo frozen
benchmark, 400 representatives (240 calibration, 160 held-out), all 200
challenge slots used, and unchanged representative core. Reviewing only
**non-held-out** human labels confirmed all 240 calibration and all 200
challenge cases have review values (including Unsure). There are 238
known-tier calibration reviews, matching the prior frozen model comparison.
All 20 new model-disagreement examples were reviewed: 7 Normal, 7 High,
6 Unsure. These challenge human labels are descriptive diagnostics only
and do not choose or tune models. Held-out human rating values have
**not** been read, audited or scored against model predictions.

All four full 600-photo score exports were successfully reconciled locally:
TOPIQ, MUSIQ, ARNIQA and the WI-0182 technical baseline each have zero
inference failures; aggregate calibration Spearman values remain
TOPIQ 0.3591, MUSIQ 0.3702, ARNIQA 0.2741, baseline 0.2343.
The maintainer's private source/score/photo JSON files **must not** be
committed. No new threshold or deployment model is justified by this result.

This checkpoint adds `original_proxy_study.py`: an explicitly opt-in,
standard-library-driven selection/report wrapper and optional local
Pillow/`pillow-heif`/`rawpy` decoder for **50–100** original-image vs
stored review-proxy pairs. Selection is deterministically balanced across
available JPEG/HEIC/PNG/RAW media families, drawn only from calibration
and challenge, without reading any human labels or held-out photos.
Full original-file bytes and proxy SHA-256 values are verified before
model loading. Selected originals are rendered to short-lived lossless
RGB PNGs for `pyiqa` and removed after scoring; per-photo scores remain
private, and only aggregate rank stability, absolute/signed shifts,
quartile overlap and decoder failure counts should be shared. Offline
tests cover blind selection, no held-out leakage, SHA verification,
path traversal rejection, failure accounting and paired scoring.

**Important limit:** production decoder and proxy creator can render
HEIC/RAW colors/tone/orientation differently from these independent
experimental decoders. No result may be described as pure downscale
sensitivity or assumed to reflect production originals exactly.
Maintainer original/proxy run is pending. Candidate checkpoint
identity/license review, explicit pre-registration of model/calibration
policy (including a likely automatic-exclusion no-go) and one-shot
held-out evaluation remain separate gates. No production code or quality
filtering changes in checkpoint 7.
