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

## Maintainer follow-up: screenshots and digitally created images (2026-10-17)

The maintainer reports that photos labelled **Unsure** in the completed blind
review were screenshots or digitally created images, rather than ordinary
camera photographs of ambiguous technical quality. This is a **domain
eligibility** finding, not evidence that the quality model has failed. It is
maintainer-observed context, not a separately verified content classification.

- Keep existing `Unsure` review values and the frozen manifest intact. Do
  not relabel these as Very Low, infer a numerical quality tier, or interpret
  them as a reason to reject otherwise usable photographic images.
- Design future **non-photographic-content eligibility** (screenshots,
  illustrations, UI/graphics and digital artwork) as a separate, explicit,
  conservative filtering decision *before* any learned photographic quality
  preference. File extension alone is insufficient: screenshots can be JPEG,
  and photos can be PNG. Do not enable such an exclusion without a separate
  verified policy, override and false-exclusion review.
- Do not retrospectively identify/filter the held-out subset or inspect its
  human labels. Out-of-domain handling for held-out evaluation must be
  predeclared before its one-time use; keep any unreviewed domain class
  undecided rather than looking at held-out labels to decide.
- The checkpoint 7 original-vs-proxy study evaluates score sensitivity to
  pixel input, *not* quality-rating accuracy or content-type classification.
  Its deterministic media-family-balanced selection does not establish that
  the selected images are all photographic content. Interpret the aggregate
  in this light.
- No new quality calibration threshold, production media-type exclusion or
  model acceptance is implied. Treat a separate non-photographic eligibility
  workitem as potential follow-up scope, not as an already-implemented feature.

The immediate next evidence remains the 50–100 paired original-versus-proxy
run, then checkpoint weight provenance and a frozen explicit acceptance or
no-go policy before evaluating held-out ratings.

## Checkpoint 7 resource-pressure follow-up (2026-10-17)

On maintainer Windows hardware, the initial TOPIQ/MUSIQ
full-resolution original-vs-proxy CPU run lasted nearly one hour
with RAM at ~99% and disk saturated. It did not expose any progress
after pyiqa model-loading because the per-photo loop had no logging.
The user was advised to cancel if sustained paging persisted.
The script is read-only for originals/benchmark and creates final
private per-model output only after all pairs finish.

Add a per-pair privacy-preserving progress log, optional 1–10 pair
probe mode and optional source-original 1601–12000px long-edge cap
(e.g. 2560) to make re-evaluation bounded and observable.
Probe exports explicitly report `isProbe=true` and cannot pass
`complete` acceptance. Capped originals are **not** full-resolution
model inputs; RAW/HEIC decoding still has some temporary
original-size memory cost. Comparisons must explicitly distinguish
capped studies from full-resolution studies and not silently replace
the original 80-photo protocol. Run a two-pair cap probe first and
evaluate memory/throughput before any second full 80-pair attempt.
No production filtering, calibration or held-out evaluation changes.

## GPU option: reuse Azure Container Apps capacity, not caption inference service

The maintainer confirmed retained GPU infrastructure from the caption
burst. The proven configuration in
`docs/operations/azure-caption-burst-bridge.md` used **Azure Container Apps
with one NVIDIA T4**, hosting an **Ollama `qwen2.5vl:3b` caption server**.
This is not an IQA endpoint and cannot score TOPIQ/MUSIQ by changing
`original_proxy_study.py --device` or pointing it at the Ollama bridge.
The Azure workload profile/registry may be reusable, but an independently
built, pinned Python/PyTorch/`pyiqa` CUDA workload and an authenticated
bounded input/output channel would be needed. GPU VRAM is not unlimited
(standard NVIDIA T4: 16 GiB); true full-resolution photos can exhaust it.
GPU use must be proven with a 1–2 image resource-limited smoke test.

**Architecture/privacy gate:** ADR-0011 authorizes only explicitly
operator-triggered remote **480×320 caption thumbnails**. It expressly
does *not* authorize sending original photos, original pixel renders,
review proxies for IQA, source paths/identifiers, or other archive data
to the Azure service. Before any remote original/proxy comparison,
require an explicit operator privacy decision and a new/updated
architecture authorization defining exactly which image bytes may
leave the PC, minimization, retention, transport security and access
restrictions. Keep PostgreSQL, identity metadata, local review labels,
paths and per-photo evaluation scores off the remote inference service.
Do not modify or endanger the working caption deployment merely
to run this experiment. Account for T4 costs and restore scale-to-zero.

**Current priority:** Observe the already-running CPU batch while
temporary per-image folders are changing; it is progressing, though
possible Windows paging makes throughput poor. PR #565's local
2-pair progress/capped-input probe remains the lowest-risk next
experiment. A remote pyIQA job is a separately scoped potential
follow-up, **not implemented or approved** by WI-0193 checkpoint 7.

## Checkpoint 8: initial full-original experiment yielded incomplete results

Maintainer completed the TOPIQ NR full-original 80-photo loop after
approximately three hours under very high memory/disk pressure.
Only **63** original/proxy pairs succeeded, **17** failed with the
legacy generic decode-or-inference error. Among the successful pairs:
17 JPEG, 20 HEIC, 20 PNG, 6 RAW. TOPIQ original-vs-proxy
Spearman **0.62356**, median signed proxy-minus-original shift
**+0.10410**, absolute median shift **0.10410**, and top-quartile
overlap **0.25**. These are incomplete/nonrandom-missing and decoder-
confounded diagnostics, not calibration or held-out acceptance.
The operator stopped MUSIQ full-original transformer inference with
Ctrl+C before its score file was written. The Hugging Face auth warning
and resource-tracker shutdown message are not proof of inference failure.

A subsequent file-presence check was run from the user profile instead
of the repository root: TOPIQ's scoring function **wrote its output before
printing the summary**. First recover that private output by checking the
original repository-root `private` directory.

This follow-up changes `original_proxy_study.py report` to show
attempts and failures by media family **on old exports**, with
stage-coded privacy-preserving failures on *new* runs. The old generic
errors cannot be retroactively disambiguated. No retry of the
resource-intensive unbounded study is requested. The 600-photo
proxy-based calibration results remain valid and unchanged, no
production model/exclusion is approved, and no held-out human labels
have been consulted. The GPU caption container is a distinct
Ollama/Azure service requiring a new pyIQA workload and separate
privacy authorization for original pixels.

## Checkpoint 9: isolate high RAW/JPEG failure rates without another IQA run

The updated maintainer aggregate report for the 80-pair unbounded TOPIQ
study is: **13/19 RAW**, **4/21 JPEG**, **0/20 HEIC**, **0/20 PNG**
failed (17 total). All failed rows still have a historical combined
`decode-or-inference-failed` code; stage attribution is impossible from
those existing exports. Among the 63 successes original/proxy Spearman
rho=0.62356, median proxy-minus-original=+0.10410 and top-quartile
overlap=0.25. This unweighted, incomplete decoder-confounded experiment
must not select a production model or override the complete 600-photo
proxy calibration evidence.

Add `original_proxy_metadata_triage.py`: a **no-model, header-only,
read-only** offline tool that verifies original content SHA and frozen
selection/score coverage and reports aggregate header readability,
megapixels and source file byte sizes for the 17 failed photos, with an
explicit opt-in for all 80 to compare failed/successful size distribution.
No human labels or held-out photos are read. Failures/exception text and
paths are not leaked. RAW header opening itself can allocate some memory;
pixel demosaicing and model inference are not performed. This identifies
possible size pressure but cannot distinguish the prior actual decode and
model inference stage. Local maintainer aggregate header triage pending.

Azure NVIDIA T4 previously used by the caption burst is *not* a pyIQA
endpoint. Any remote original/proxy scoring still requires a separately
authorized, secure pixel transport and pinned CUDA model workload,
not reuse of the existing Ollama thumbnail exception. Defer additional
full-res CPU runs and keep automatic exclusion at no-go.

## Checkpoint 10: original-resolution diagnostic and selected evaluation input

The maintainer's complete 80-photo header-only comparison found all **17/17** original-pixel TOPIQ failures were images over 40 megapixels (JPEG 4/4 failed around 50 MP; RAW 13/13 failed around 49 MP). All **63/63** originals that had scored successfully were at most 24.47 MP (JPEG 17, HEIC 20, PNG 20, RAW 6). All 80 source headers were readable and passed frozen original SHA verification. No photos between 24.47 and 48.77 MP were represented, so this does not prove a numerical failure cutoff or whether decode or model inference caused the failures. The high-resolution result is incomplete (63/80 pairs), decoder-confounded, costly and unsuitable as acceptance evidence.

**Evaluation input decision:** retain the frozen `jpeg-1600-q78` review proxy as the WI-0193 model input, including for the one-shot future held-out evaluation. Four existing proxy candidate exports cover all 600 benchmark photos with zero inference failures. No full-resolution CPU retry or new Azure GPU work is justified as a prerequisite to the proxy-based model assessment. An optional long-edge cap currently applies after decoding and does not guarantee low memory use for full-resolution RAW decoding. Remote original-image inference remains outside the caption-only Azure authorization.

Next: capture model checkpoint hashes, mapping and third-party licensing; freeze a versioned calibration-only ordinal map and either a candidate choice or explicit no-go before consulting held-out human ratings; evaluate the held-out set once. The calibration sample has only three Very Low ratings and automatic exclusion remains **no-go**. This checkpoint does not select or deploy a production model, change any held-out labels, or commit private image/score data.

## Checkpoint 11: checkpoint provenance and calibration-only draft preparation

Following acceptance of the `jpeg-1600-q78` proxy as the fixed M37
evaluation input, introduce a *non-inference*, fully local provenance
preflight and an explicitly unapproved calibration-only mapping
proposal. This checkpoint has **not** selected a candidate or
unblinded the held-out benchmark.

- `checkpoint_provenance.py --cache-root` inventories downloaded
  PyTorch/pyIQA weight artifact basenames, file hashes and byte sizes,
  plus local Python, pyIQA, torch, torchvision, Pillow, rawpy and
  pillow-heif versions. File-name-derived TOPIQ/MUSIQ/ARNIQA hints
  are **not** a verified model-to-artifact mapping. The older explicit
  `--checkpoint name=path` form remains available and records
  **not-verified** license/mapping status. Neither mode loads weights,
  downloads from the network, reads private photos or grants
  deployment rights. Third-party checkpoint licenses must be checked
  independently; package license is insufficient.
- `calibration_policy_draft.py` checks exact benchmark/score coverage
  and immutable proxy input, then examines **only** reviewed, known-tier
  representative calibration labels. For a manually supplied candidate
  export, it calculates monotone Low/Normal/High median anchors and
  their two absolute raw-score midpoint thresholds. The resulting
  coarse **three-band** ordinal proposal is a diagnostic starting
  point, not the full five-tier acceptance target. It rejects missing
  or non-monotone anchor evidence and reports in-sample confusion
  without per-photo rows. No held-out or challenge ratings are
  accessed. Tests deliberately mutate held-out/challenge ratings to
  verify invariance.
- Every draft is marked `research-draft-not-frozen` and hardcodes
  `heldOutEvaluationAllowed=false`,
  `productionModelApproved=false`, and
  `automaticVeryLowExclusionApproved=false`. It does **not**
  select between MUSIQ/TOPIQ, assert a valid weight license, freeze
  any policy, permit final held-out evaluation or enable automatic
  exclusion.
- The maintainer should run local cache inventory and separate
  candidate drafts with the existing 600-photo score exports, then
  share **aggregate-only** console evidence and independent weight
  licensing verification. Only in the *next* checkpoint, after
  independent mapping/licensing review, should a candidate/mapping
  decision be explicitly sealed or rejected **before** the one-time
  held-out evaluation. The held-out labels remain untouched.

No production code, archive data, score exports, photo rating labels,
or app collection/search/exclusion behavior changes in checkpoint 11.
