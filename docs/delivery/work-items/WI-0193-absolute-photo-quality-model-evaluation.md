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

### Upstream framework license research checkpoint (2026-10-18)

The official PyPI release metadata for the locally reported
`pyiqa 0.1.16` lists license expression
`PolyForm-Noncommercial-1.0.0`:
https://pypi.org/project/pyiqa/0.1.16/.
The IQA-PyTorch upstream repository also references NTU S-Lab
licensing for applicable components:
https://github.com/chaofengc/IQA-PyTorch.
These statements do **not** establish the license of an individual
TOPIQ, MUSIQ or ARNIQA pretrained checkpoint; its source and rights
must be checked independently. Do not bundle research-only framework
code or weights into Photo Identity for general production use before
reviewing permission for the intended use/distribution. Experimental
benchmarking is not production deployment approval.

## Checkpoint 12: corroborated TOPIQ/MUSIQ cache identities and incomplete ARNIQA provenance

The maintainer ran the checkpoint 11 local cache inventory:
`pyiqa 0.1.16`, `torch 2.14.1`, `torchvision 0.29.1`,
Pillow `13.0.0`, rawpy `0.27.1`, pillow-heif `1.8.1`.
Three cached artifacts were reported under PyIQA's local cache:
TOPIQ `cfanet_nr_koniq_res50-9a73138b.pth`
(SHA-256 `9a73138beb3748dd6a83365610ac88fa1cd53225342c77fc9ed44a3be461354c`,
181196009 bytes); MUSIQ `musiq_koniq_ckpt-e95806b9.pth`
(SHA-256 `e95806b9eae5f3814c410f574ba8e552362bd5bc63d758ed5b97860f5d6185aa`,
108610983 bytes); and `regressor_koniq10k.pth`
(SHA-256 `af8f127aca38a8e1082e066b5ee93e533bf5f33aaf9c76b3c31526ef901919e1`,
19560 bytes).

TOPIQ/MUSIQ filenames agree with published PyIQA default model
configurations and with earlier local console loading messages,
but the weight-to-model mapping remains **inferred, not
independently proven from the running environment**. ARNIQA additionally
uses a learned encoder `ARNIQA.pth` and may use a torchvision
ResNet50 pretrained backbone; its small cached KonIQ regressor is
not a complete model. To avoid misleading provenance completeness,
extend offline cache discovery with optional extra explicit
Torch checkpoint roots and unverified encoder/regressor/backbone
role hints. Do not load models or consult any photos/ratings.

Neither the package's published noncommercial license expression
nor ARNIQA's own repository Apache-2.0 designation alone determines
the rights for all specific pretrained weight files. Licensing
remains **unverified** for deployment and must be resolved before
any production go decision. No private file paths, benchmark or
score rows were committed.

The next maintainer task is to run `calibration_policy_draft.py`
independently for TOPIQ and MUSIQ using the existing complete
600-photo immutable proxy scores. This is a fast **calibration-only,
no-inference** operation: the produced three-band drafts are explicitly
not frozen, do not authorize held-out label evaluation and do not
enable Very Low exclusion. The policy freeze and final one-time
held-out evaluation remain separate subsequent checkpoints.

## Checkpoint 13: midpoint three-band drafts fail naive absolute-accuracy baseline

Maintainer completed both **calibration-only** drafts from the existing
600-photo frozen proxy score exports, with 238 rated representative
calibration photos. Low/Normal/High median-anchor sample counts
were 38/154/42 for each. The three-band truth counts are
Low-or-below=41 (3 Very Low + 38 Low), Normal=154,
High-or-above=43 (42 High + 1 Very High).

| Draft candidate | Fixed oriented thresholds | Correct / 238 | Apparent calibration accuracy |
| --- | --- | ---: | ---: |
| TOPIQ NR 0.1.16 | 0.4976193383336067, 0.5708234310150146 | 89 | 37.39% |
| MUSIQ 0.1.16 | 63.076544761657715, 69.06219482421875 | 90 | 37.82% |
| Constant Normal reference | no score thresholds | 154 | 64.71% |

The constant-majority classifier **cannot distinguish images**;
nevertheless, the median-midpoint three-band proposals have very poor
apparent absolute accuracy *on their own calibration data*. The one
photo (0.42-point) advantage for MUSIQ is not candidate-selection
evidence, especially because these drafts lack a paired prediction
comparison. Neither proposal should be frozen/deployed. These
findings do **not** overturn separate candidate ranking evidence:
TOPIQ 83.8% / MUSIQ 82.8% pairwise ordering for human ratings
separated by at least two tiers, with calibration-only Spearman
rho 0.359 / 0.370. Different endpoints answer different questions.

Checkpoint 13 introduces `three_band_calibration_diagnostic.py`,
which reads **only the aggregate 3x3 confusion matrices** already
present in the two saved private calibration drafts. It validates
same benchmark/calibration-label fingerprints, three-band coverage,
consistent actual counts, and all model/held-out/exclusion permissions
still false. It reports class prediction counts, precision, recall,
macro F1, balanced accuracy, extreme-cross-band errors and a
constant-majority accuracy baseline. It neither reads the benchmark
manifest or per-photo score rows nor accesses **any** held-out or
challenge human labels. Its aggregate report is descriptive,
in-sample, and **not** cross-validated.

Do not automatically optimize thresholds against all 238 calibration
photos and misrepresent the resulting accuracy as unbiased. The
classification failure motivates examining calibration confusion
and then choosing between (a) a documented ranking-only/no
absolute-classification recommendation, (b) a separately defined
and validated conservative abstaining quality preference, or
(c) an explicit WI-0193 model no-go. Every production/deployment
choice still needs licensed weights and a frozen analysis policy.
Automatic Very Low exclusion remains **no-go**.

## Checkpoint 14: reject midpoint absolute tiers; bound the remaining ranking-only question

The maintainer completed the confusion diagnostic using saved private
calibration-only drafts, with **238** reviewed representative
calibration photos. **No held-out/challenge ratings were accessed.**

| Metric | TOPIQ NR | MUSIQ |
| --- | ---: | ---: |
| Three-band correct | 89 / 238 (37.4%) | 90 / 238 (37.8%) |
| Balanced accuracy | 49.9% | 48.5% |
| Macro F1 | 38.4% | 38.9% |
| Normal recall | 36/154 (23.4%) | 40/154 (26.0%) |
| Predicted High-or-above | 102 | 102 |
| High-or-above precision | 25/102 (24.5%) | 21/102 (20.6%) |
| Low-or-below precision | 28/79 (35.4%) | 29/73 (39.7%) |
| Extreme Low/High cross-band mistakes | 10 | 11 |

The all-Normal baseline correctly labels **154/238 (64.7%)**,
but does not distinguish photographs. Both current fixed median-midpoint
mappings fail at *absolute* quality-band classification, including on
their own calibration set. **Do not freeze either mapping**, use these
scores to exclude images, or imply the tiny one-photo delta is meaningful.
The earlier >80% two-tier-separated *pairwise ordering* evidence
measures a different objective and leaves one narrower possibility:
bounded, optional quality **preference** based on large candidate
score differences. Changing Search/Smart/Creative/Slideshow production
ranking remains **outside WI-0193 implementation scope**.

Checkpoint 14 adds a research-only `calibration_selective_ranking.py`
diagnostic. It uses the two already-complete 600-photo proxy score exports,
but reads human labels **only** on 238 known-tier representative
calibration photos. It deterministically divides calibration images
into five disjoint photo groups (exact content-SHA duplicates stay
together) and predeclares score-gap quantile rules `0/50/75/90%`.
For each fold, only **training-fold raw-score pair gaps** determine
the cutoff; on validation-fold *within-fold* pairs, it counts how
often score-implied ordering matches human ordering among different
human tiers, including a separate >=2-tier subset. It reports
decision coverage among all pairs plus ordering accuracy on
comparable subsets; abstaining at higher score gaps may or may
not improve reliability. No raw-score-to-class threshold is
optimized and no mapping is frozen.

**Caveats:** the five folds are internal calibration diagnostic
partitions, not the sealed 160-photo held-out partition. Pair samples
share photos and are not independent; near-duplicates can survive
the exact-content grouping. A predeclared quantile sweep performed
on one small calibration dataset is exploratory; choosing the best
quantile afterward does not prove deployment benefit. No GPU,
IQA model inference, photo originals, proxy pixels, held-out/challenge
human labels or production behavior are accessed. Very Low auto
exclusion remains no-go and weight licenses remain unresolved.

Next: use the calibration-only aggregate output to decide whether
this question merits one **explicitly frozen** ranking-only
research hypothesis for one-shot held-out testing, or record a
conservative **no-go** and close WI-0193 without unsealing held-out
labels. Do not introduce a new production ranker in this checkpoint.

## Checkpoint 15: five-fold calibration evidence supports a conditional ranking-only research hypothesis

The maintainer completed the checkpoint-14 score-gap study with **238
representative calibration photos**, divided into deterministic
five-fold photo-disjoint subsets (39, 49, 49, 57, 44). Thresholds
were estimated using training-fold raw-score differences only;
validation evaluated within-fold photo pairs. The study did not
read held-out/challenge human labels.

| Quantile of score-gap rule | Approx. comparison coverage | TOPIQ ordering when human tiers differ | MUSIQ ordering when human tiers differ |
| --- | ---: | ---: | ---: |
| 0% (no abstention) | 100% | 69.87% | 71.24% |
| 50% | 50% | 80.98% | 82.26% |
| 75% | 25% | 90.27% | 89.75% |
| 90% | 11% | 97.41% | 96.86% |

For human ratings separated by at least two tiers, TOPIQ 75%
selected **247/466** eligible calibration-fold pairs and ordered
**92.71%** correctly. MUSIQ 75% selected **276/466** and ordered
**89.49%** correctly. These differences should **not** be treated
as statistically significant; photo pairs reuse observations,
and same-tier pairs are excluded from the correctness denominator
even though the actual decision rule cannot identify their tiers.
All-pair coverage at TOPIQ 75% was **1441/5635 (25.57%)**,
and accuracy among human-different-tier selected pairs was
**~90.27% of 935**.

**Research choice, not deployment approval:** advance a **single
TOPIQ NR score-gap 75th-percentile** hypothesis to a *review-only
pre-registration proposal*. TOPIQ exceeds 90% observed ordering
among different-tier pairs at approximately one-quarter decision
coverage, and performs modestly better than MUSIQ on the more
strongly separated 2+-tier comparison at this setting. MUSIQ
remains a reasonable alternative on runtime grounds (roughly
2.7 s/photo proxy vs TOPIQ 4.4 s/photo CPU), but testing both
and choosing afterward would expand selection bias. The chosen
cutoff was selected after seeing multiple calibration candidates/
quantiles and must be described as an **exploratory selection**,
not pre-registered independent validation.

The new `ranking_freeze_proposal.py` computes **one scalar,
deterministic oriented-score difference threshold** from the
75th percentile of all unordered raw-score gaps over the original
238 *known-tier representative calibration* TOPIQ scores (28203
pairs), **without using rating magnitudes to choose the threshold**.
The chosen score-export hash and a calibration-review fingerprint
are stored in a new private, exclusively created draft. Neither
the study's five fold-specific cutoffs nor a per-photo absolute tier
is the proposed policy. The proposed pairwise rule prefers a photo
only when its score exceeds the other's by at least the fixed
positive gap; otherwise it abstains. **No image is excluded**.

The proposal is explicitly marked
`status=review-proposal-not-frozen`,
`researchHypothesisFrozen=false`,
`heldOutEvaluationAllowed=false`,
`productionModelApproved=false` and
`automaticVeryLowExclusionApproved=false`.
Model-to-weight attestation and checkpoint/framework permissions
remain separate, unresolved deployment gates. The full private
benchmark, score exports and held-out reviews remain untouched.

**Next decision:** once the maintainer supplies the aggregate
threshold proposal, review the fixed rule, absolute score units,
evidence fingerprints and research success/failure endpoint *before*
explicitly freezing any one research hypothesis. A possible
predeclared held-out criterion is correct ordering >=80% among
human different-tier selected pairs at >=20% all-pair coverage,
with >=80% ordering of 2+-tier selected pairs. Report both raw
counts and photo-resampled intervals, same-tier decision rate,
and failure/abstention. These are research criteria, **not
automatic production admission**. The one-time sealed 160-photo
held-out evaluation must only be implemented/authorized in
a separate work stage and **never run as part of this PR**.

No production ranker, archive evidence, rating, automatic exclusion,
Search/Smart/Creative/Slideshow behavior or licensing status is
changed in this checkpoint.

## Checkpoint 16: TOPIQ ranking-only research freeze approved; held-out still sealed

On **2026-10-18**, after the maintainer's successful offline checkpoint-15
proposal, the maintainer explicitly instructed: **freeze TOPIQ with the
suggested threshold**. The decision and the research success criteria were
recorded in issue #536. This is not approval to consult held-out human
ratings, deploy an IQA model or exclude images automatically.

The single frozen hypothesis is `pyiqa-topiq_nr-0.1.16` using
`jpeg-1600-q78` proxies, with higher score better and exact positive
absolute score-gap cutoff **`0.16755089163780212`** (75th percentile
of the 28,203 unordered score gaps from 238 calibration photos).
When the gap is smaller, or zero, **abstain**; when it meets the
cutoff, prefer the higher-scoring photo. Never change this number after
seeing held-out results. No absolute five-tier/three-tier labels, Very
Low exclusions, production ranking or photo filtering are sanctioned.

Predeclared **held-out research** criteria on the 160 representative
held-out photos: >=80% correct different-tier ordering on selected
pairs (and >0 such pairs), >=20% all-pair preference coverage, and
>=80% correct selected comparisons >=2 human tiers apart (and >0).
Report counts, abstentions, same-tier decisions and photo-level uncertainty;
do not treat pairwise comparisons as independent. Candidate and quantile
came from exploratory calibration screening, so the freeze does not remove
selection bias. Only one held-out evaluation is permitted after a later,
**separate explicit** authorization.

`ranking_freeze.py` creates a **private, local** attestation of this
approval. It verifies the previously saved proposal against the
complete benchmark/score export, checks private calibration/score
fingerprints, and insists on the exact cut-off and candidate.
The original private proposal stays unchanged and correctly reports
`review-proposal-not-frozen`. The new attestation sets
`researchHypothesisFrozen=true`, but retains
`heldOutEvaluationAllowed=false`, `heldOutLabelsAccessed=false`,
`productionModelApproved=false`,
`automaticVeryLowExclusionApproved=false`,
`checkpointLoadedIdentityVerified=false`, and
`frameworkAndCheckpointLicenseApproved=false`.

The next checkpoint must implement and review a one-shot gated
held-out evaluator; it is **not** run or authorized in checkpoint 16.
The maintainer next runs the offline attestation command from the
operations runbook and shares its aggregate console JSON. Neither
private calibration evidence nor held-out ratings are committed.

## Checkpoint 17: sealed preflight and reviewable one-shot held-out evaluator

The maintainer completed the checkpoint-16 **private frozen** TOPIQ
ranking attestation, with exact `0.16755089163780212` positive
raw-score gap and the previously frozen 80%/20%/80% research criteria.
The maintainer's aggregate output still says
`heldOutEvaluationAllowed=false` and
`heldOutLabelsAccessed=false`. This is correct and must not be
silently promoted into a held-out run.

Checkpoint 17 prepares `heldout_ranking_one_shot.py` with:

- **Sealed preflight only** as the permitted local next action. Validates
  the existing calibrated proposal and frozen attestation, exact
  existing 600-score export, 240/160/200 partition counts and
  held-out representative cohort identity. Computes binding
  fingerprints *without reading held-out/challenge human reviews*.
- **Separately gated evaluate mode** for a *future* authorization.
  Requires a strict private operator authorization record bound to
  the sealed preflight fingerprint, and one exclusive durable
  one-shot claim next to the frozen attestation before any held-out
  `qualityLevel` is examined. Does not create an authorization
  artifact or override the frozen `heldOutEvaluationAllowed=false`.
  Claim reuse is rejected. A post-claim failure must be investigated
  rather than retried or used to retune.
- One predeclared report from the 160 representative held-out photos
  (12,720 all-pair potential comparisons), with ordered/abstained,
  same-tier, unsure, two-or-more-tier conditional numerator and
  denominator counts, threshold pass/fail and seeded photo-level
  cluster bootstrap 95% descriptive intervals (1,000 repeats).
  Comparisons reuse photos and are not independent observations.
- Synthetic-only offline tests for sealed review-label invariance,
  tampered evidence, strict authorization mismatch, selected/abstained
  ordering, missing tiers, same-tier decisions, bootstrap and
  single-use claim.

The command in the operations runbook is **preflight only**, safe to
run after checkpoint 17 PR merges; the maintainer must share its
aggregate console result before any separate held-out permission
is considered. No one-time held-out evaluation is performed in this
PR. No production inference, score persistence, auto exclusion,
Search/Smart/Creative/Slideshow changes or licensing approval.

## Checkpoint 18: one-time held-out evaluation finished — selective ranking research success, absolute tiers no-go

On **2026-10-18**, after PR #583 merged and a sealed local preflight
passed, the maintainer separately authorized the one-time held-out
TOPIQ evaluation. The maintainer ran `heldout_ranking_one_shot.py
--mode evaluate` **once**, shared only the aggregate console result,
and reported `status=held-out-research-evaluated-once`. The evaluation
used the existing complete `pyiqa-topiq_nr-0.1.16` immutable
`jpeg-1600-q78` review-proxy scores with **the exact frozen
absolute raw-score gap `0.16755089163780212`**. No new inference
or cutoff fitting was performed.

The 160 representative held-out photos included 157 with known
five-tier human reviews, three unsure/unrated, and zero score failures.
Of **12,720** unordered photo pairs, **4,157** met the preference
cutoff (32.68% coverage) and **8,563** were abstained. There were
12,246 known-tier pairs and 474 pairs with at least one unknown tier.
Among 6,185 same-tier pairs, 1,438 were selected (23.25%); these
are **not** counted as correctly ordered.

| Predeclared held-out endpoint | Observed count and rate | 95% photo-bootstrap interval | Point-estimate criterion |
| --- | --- | --- | --- |
| Selected different-tier ordering (>=80%) | 2,236/2,581 = **86.63%** (of 6,061 different-tier pairs) | **75.07–93.69%** | PASS |
| Preference coverage among *all* unordered pairs (>=20%) | 4,157/12,720 = **32.68%** | **26.50–37.85%** | PASS |
| Selected >=2-tier-separated ordering (>=80%) | 740/760 = **97.37%** (of 1,111 >=2-tier pairs) | **89.92–99.81%** | PASS |

The three accuracy/coverage checks were defined before accessing
held-out human labels, and each succeeds on its **point estimate**;
`allResearchCriteriaMet=true`. Uncertainty came from **1,000 seeded
photo-cluster resamples** (seed 20261018), not independent
pair-resampling. **Critical qualification:** the 95% interval for
overall different-tier selected ordering extends **below the
80% target** (lower bound 75.07%). Consequently, the criterion was
met descriptively on this held-out cohort, not demonstrated to
exceed 80% with high confidence across new photo samples.
The >=2-tier selected accuracy interval clears 80%.

### Bounded model research decision

- **RESEARCH GO:** preserve the frozen TOPIQ p75 **selective pairwise
  quality preference with abstention** as a validated, research-level
  finding for clearly different photographs. The 97.37% rate is
  conditional on large gaps and >=2 human quality tiers, which
  cannot be known at runtime. It is not 97.37% general photo-ranking
  accuracy, a guarantee, or evidence of absolute quality tiers.
- **ABSOLUTE QUALITY CLASSIFICATION NO-GO:** prior coarse three-band
  median-midpoint TOPIQ and MUSIQ mappings underperformed a trivial
  all-Normal reference on calibration. Only three calibration reviews
  fell in Very Low. **No automatic Very Low exclusion** and no usable
  five-tier single-photo quality classifier were demonstrated.
- **PRODUCTION NO-GO / DEFER:** checkpoint-weight identity/licensing,
  proxy-vs-original domain effects and runtime/deployment economics
  remain separate gates. No archive writes, quality persistency,
  collection/search/slideshow ranking, auto rejection or production
  weight redistribution is approved or introduced by WI-0193.
  Future user-experience experiments require **new workitems and
  independent data/evidence**, not further cutoff tuning on
  this already-accessed held-out cohort.

This consumes the **one authorized held-out use**. Do **not** delete
the private one-time claim, rerun the evaluator, alter the frozen
rule, switch to MUSIQ for an additional held-out test, or optimize
against the held-out reviews. Preserve private authorization, claim,
report and preflight files unchanged. Only aggregate results are
entered into the repository. WI-0193 may be concluded after this
decision is merged; follow-on optional preference/rights work
is outside this evaluation workitem.
