# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read the target shard and linked document before continuing.

## Current focus

**2026-10-30 M30 / WI-0189:** Active development on
`feature/wi0189-proxy-cache-retention-foundation`, PR #665.
Checkpoint 1 introduces non-deleting, deterministic bounded proxy
retention planning and isolated tests. Next: catalogue-backed ownership,
lease-fenced safe eviction, configurable quota/profile and real MOV/MP4
quality/integrity verification. Keep WI-0189 `in_progress` until full
acceptance; WI-0207 online-only OneDrive hydration remains separately
proposed and unverified. Run the docs generation/validation and focused
tests in CI before merging this checkpoint.

**2026-10-22 repository-only planning / issue-retirement cleanup:**
Audit all 33 surviving GitHub issues (12 open, 21 closed) and retire them
only after the canonical documentation PR merges and generated views validate.
[Issue-retirement audit](docs/delivery/retired-github-issues-2026-10-22.md)
maps each issue to its work item, ADR/runbook or explicit accidental status.
New [WI-0205](docs/delivery/work-items/WI-0205-continuous-photo-quality-rating.md)
under M40 preserves the **proposed** continuous photo rating from #639.
New [WI-0206](docs/delivery/work-items/WI-0206-native-quality-suggestion-review.md)
under M40 preserves **completed** native Archive reviewer acceptance from #629.
Existing M36 and M38 delivery work remains in its unchanged nonterminal
canonical YAML, even after its historical issues are deleted. Do not create
new GitHub issues or infer work-item acceptance from issue deletion.

**2026-10-22: restore repository-native Creative curation tracking.**
The former issue-only feature labelled **WI-0638** in merged PRs
#647, #651, #653 and #654 now belongs to canonical
[WI-0203](docs/delivery/work-items/WI-0203-creative-collection-similarity-review.md)
under new [M39](docs/delivery/milestones/M39-creative-collection-curation.md).
The initial private pilot passed 11/11 checks, the compact UI was accepted as
better, and later burst-review groups were judged *much better*. WI-0203
awaits final latest-package verification. These review choices are still
**annotations only**, with NO effect on Creative selection or slideshow
snapshots; M37 unattended/global suppression remains NO-GO.
[WI-0204](docs/delivery/work-items/WI-0204-reversible-collection-scoped-alternative-omission.md)
owns the not-yet-developed, explicitly opted-in reversible **collection-only**
omit/restore feature. **Never create GitHub issues as planning work items
again.** Review the work-item YAML and operator runbook for next steps.
Historical context below is archival and not the current handoff.

## Current focus

**M37 formal disposition (2026-10-21):** Research evaluation
COMPLETE with explicit NO-GO/DEFER decisions. WI-0192 and
WI-0193 completed; WI-0194 COMPLETED as archive-wide
visual-redundancy candidate discovery + 200 calibration /
180 guarded heldout human-pair evaluation, with **NO-GO**
to unattended similarity suppression. WI-0195
CLOSED as research disposition, original four-arm NOT DEMONSTRATED in its original FOUR-ARM automatic
policy acceptance: bounded 80-photo live Creative shadow
and 6+8 synthetic tests passed and 80/80 proxy plan
worked, but 0/80 prior TOPIQ score overlap and no
third-party weights/inference authorized. Production TOPIQ
quality preference DEFER; automatic Very Low and combined
suppression NO-GO. Human manual technical suggestions
were genuinely useful in first pilot (129 reviewed,
20 Consider exclusion, most unwanted as judged by
maintainer). Issue #629 remains active for native
product; PR #633 merged (native UI, durable local
decisions, context review toggles); **local native
smoke test still required**. Never conflate review
context toggles with production collection/slideshow
filtering; no auto purge. Canonical M37 closure memo:
docs/delivery/milestones/M37-decision-ledger.md.
Do not reopen consumed WI-0193 heldout labels or
indefinitely extend M37 with further scoring probes.

**2026-10-21 WI-0629 native manual suggestions:** First merged
manual research companion generated 129 automatically suggested
technical-quality candidates and operator reviewed ALL in Archive
cleanup (72 Keep, 37 Dismiss, 20 Consider exclusion). Operator
confirmed MOST considered photos genuinely unwanted, review
reasonably convenient. **GO to a native manual quality suggestion
experience** separate from M37's automatic Very Low/redundancy
NO-GO. Current branch adds a native /quality-suggestions Blazor
page; bounded explicit scan of existing current SHA-checked
jpeg-1600-q78 review proxies using WI-0182 technical reasons,
no new model or originals; local app-data durable reviewer
choices and independent Archive/Collection/Slideshow review
settings; Consider exclusion follow-up view linking existing
native Photo viewer for separately confirmed existing Exclude
& purge. No auto purge, no changes in Search/Smart/Creative/
Slideshow. Existing companion private human review records
remain private and are NOT silently imported; native scan
begins new standalone queue. On merge, one native end-to-end
verification with restart and per-context isolation. See
docs/operations/native-quality-suggestion-review.md and
issue #629. M37 record research-only TOPIQ collection DEFER,
automatic Very Low/redundancy/combined NO-GO, human suggestion
feasibility GO, old WI-0195 four-arm acceptance unmet.

**2026-10-20 M37 manual suggestions pivot:** First WI-0195 bounded
plan passed locally (6+8 tests, 80/80 verified current proxies,
0 matching original TOPIQ scores, prospective 80 new CPU scores,
**no inference**/no writes). Maintainer clarified the real
product requirement is **automatic poor-photo suggestions with
operator-selected manual exclusion**, false positives acceptable
because decisions are manual, and independent per-use-case
controls (Archive cleanup vs collections vs slideshows).
Existing ADR-0008 Photo Identity exclusion is a CONFIRMED
source-copy-local purge of stored derivatives/metadata and is
NOT a reversible context-specific hide. New issue #629 and
`docs/operations/manual-quality-suggestions.md` contain
first non-destructive local reviewer: bounded existing verified
proxy technical scan, explained ranked candidates, private
Keep/Dismiss/Consider decisions + separate context review modes,
no API-based exclude and no production collection/slideshow
changes. M37 no-go for unattended Very Low/visual suppression
remains. This is a bounded product UX validation before
larger native in-app work, not a full policy acceptance.
PRs target main.

**M37 final research evaluation path (2026-10-20):** This
milestone concludes with defensible GO/NO-GO/DEFER decisions,
not required production deployment. Decision ledger is
`docs/delivery/milestones/M37-decision-ledger.md`. WI-0192
and WI-0193 research concluded; WI-0194's 200 calibration
and 180 selected guarded heldout pair reviews led to NO-GO for
automatic redundancy/combined suppression. WI-0193 TOPIQ
`0.16755089163780212` p75 score-gap is research-only; NO
absolute quality tier, Very Low auto-exclusion, or deployment
model license approval. First WI-0195 maintained Creative
preview test passed 8 tests but 0/80 old 600-photo TOPIQ score
coverage, thus no meaningful A/B review. ONE substantive
bounded WI-0195 phase now adds private, read-only current
proxy mapping (<=240 revision IDs), NO-INFERENCE default
coverage/time/rights plan, separately authorized local
pyiqa 0.1.16 TOPIQ research scoring <=120 new photos, and
reuse existing frozen same-kind/relevance shortlist shadow +
private blinded Finish & Report, with multi-preview support.
No original hydration, consumed human held-out labels,
catalogue writes, selection API changes or production model.
Local operator should first RUN PLAN and share aggregate;
no inference permission presumed. After one bounded result
make explicit final 4-policy disposition and close M37 as
evaluation/rejection/defer with unmet four-arm checklist
clearly acknowledged. Stop micro-checkpoint loop. PR -> main.

**M37 WI-0195 research-only shadow development (2026-10-20):**
WI-0194 reviewed 200 calibration + 180 selected held-out pairs;
automatic redundancy and combined suppression remain explicit
NO-GO (Wilson lower bounds below 95%, missing held-out
far-dHash/high-CLIP disagreement, nonrepresentative sampling).
WI-0193's TOPIQ p75 exact 0.16755089163780212 gap is
RESEARCH-ONLY pairwise preference, with NO absolute quality
classifier, Very Low exclusion or production model/license approval.
Original WI-0195 four-arm acceptance stays blocked. This branch
introduces one bundled OFFLINE/READ-ONLY shadow probe of
captured Creative/Search-anchored preview shortlists: same kind
and same existing relevance score contiguous positions only;
no missing-score inference, no held-out quality labels, no
automatic suppression. Existing 600-photo scores are sparse;
report all coverage and abstention. Private score-blind A/B
plus audit, optional current-proxy map, local Finish & Report
browser, aggregate-only preferences, safety tests, one PowerShell
runner. Zero collection/snapshot/cataloque mutation. Do not
mark original WI-0195 acceptance complete. PR targets main.

**M37 WI-0194 independent validation bundle (2026-10-19):**
Maintainer finished all 200 calibration labels with 82 Redundant,
17 Similar-keep-both, 101 Not-similar, 0 Unsure. dHash0-4 and
dHash5-8 sampled buckets each 23/24 Redundant; dHash9-12 only
8/24; high-CLIP-other 18/20; far-dHash/high-CLIP 9/20.
These are correlated, intentionally stratified CALIBRATION
observations, not held-out production precision. No suppression
threshold approved. Next single substantive PR bundles a
frozen-before-labels held-out pair study, with exclusions only
for observed cross-split dHash5-12 or CLIP>=.90 links whose
calibration endpoint belongs to the 382 *reviewed calibration*
photos, guard against >20% heldout quarantined or <100 selected
heldout candidate pairs, distinct blinded/audit/review files,
read-only current-proxy map requiring frozen validation audit,
explicit local heldout reviewer authorization, synthetic tests
and one PowerShell runner for preparation and optional review.
No labels in gate, no changed production behavior/ANN/new
model or access to WI-0193 consumed quality heldout labels.
WI-0195 blocked pending explicit decision. All PRs target main.

**M37 WI-0194 bundled phase (2026-10-19):** Maintainer
requested an end to repeated tiny-PR + one-command verification
cycles. M37 WI-0192/0193 are complete; WI-0195 depends on an
accepted WI-0194 visual-redundancy result and is blocked.
Checkpoint-8 dry-run has 200 calibration-only candidate pairs,
382 distinct calibration photos, 0 held-out; 9/9 proposed
candidate buckets met, without human labels. Prior full
27,050-proxy archive dHash/seeded CLIP feasibility and candidate
supply accepted. Held-out blanket similarity quarantine and
dHash<=8/12 global linkage rejected as biased/giant-family.
This integrated WI-0194 branch implements full CALIBRATION-ONLY
pilot: deterministic blinded 200-pair private bundle, read-only
current local proxy resolver, loopback two-photo human reviewer
with hash validation/notes/resume, bound descriptive aggregate
report, synthetic safeguards, and ONE PowerShell local runner.
Preparation is safe/no labeling by default; -Review is explicit.
No held-out review access/freeze, no model deployment/selection,
quality-heldout reuse, automatic suppression, collection
semantics, original hydration or ANN. WI-0195 blocked until a
separate accepted policy or explicit milestone no-go.
All PRs target main.

**WI-0194 checkpoint 8 (2026-10-19):** Maintainer's
checkpoint-7 observed-risk held-out quarantine sensitivity found
NO-GO: dHash5-8 quarantine removes 470/6901 held-out photos
but leaves only 38/50 disagreement candidates; stronger
dHash5-12 removes 1689 and leaves 7/50; add high CLIP and
2055 photos (29.8%) are excluded leaving 5/50 disagreements.
No general family expansion or blanket photo quarantine
satisfies representative review requirements. The original
dHash<=4 photo split remains unfrozen research baseline;
the enriched 165,181-pair archive retains sufficient baseline
borderline/negative candidates but genuine exact-copy
controls are absent. New read-only aggregate-only
`redundancy_calibration_pilot_preflight.py` studies a bounded,
diversity-capped CALIBRATION-ONLY unlabelled dry run
(nine heuristic risk strata), never creates review manifests
or uses held-out-only/cross-partition pairs. After PR merge
operator runs Python synthetic tests and preflight on existing
private enriched manifest; shares aggregate stdout only.
NO human review, split freeze, model/threshold authorization,
quality-heldout reuse, ANN, or production changes. WI-0195 blocked.

**WI-0194 checkpoint 7 (2026-10-19):** Maintainer accepted
full-proxy opt-in candidate enrichment: 27,050 photos, 164,381
retrieved candidate pairs, 800 screened negative candidates,
1,552 additional mid-similarity pairs; held-out borderline
129/60 and screened random negatives 51/35, all non-exact-copy
quotas now met. Genuine exact-copy controls are absent.
Strict dHash<=4 families unchanged; observed cross-partition
dHash5–12 and high CLIP risks persist. No-go for review-set freeze.
New diagnostic `redundancy_heldout_quarantine_preflight.py`
quantifies held-out photo/candidate supply remaining after
four predeclared risk-exposure exclusion sensitivities on
the same unchanged split. No labels, outputs, model download,
production changes, ANN or approval; quarantine can bias
held-out sample and cannot prove missing near-copy absence.
After PR merge operator runs offline Python synthetic tests,
then aggregate-only private enriched-manifest preflight.
WI-0195 remains blocked; all PRs target main.

**WI-0194 checkpoint 6 (2026-10-19):** 27,050-photo full-archive
dHash/CLIP research proved exact offline retrieval practical.
Maintainer's private sensitivity study found dHash<=8 family
of 305 photos and <=12 family of 3,368, with held-out
borderline 19/60 or 15/60 and screened controls 10/35.
Both partitions have zero genuine exact-copy controls.
No-go for leakage-family expansion or human cohort freeze.
Opt-in local CLI candidate supply adds 16 deterministic
measured CLIP borderline candidates per seed (dHash>24,
cosine [.62,.78)) and up to 800 screened random controls;
defaults and private schema unchanged. After PR merge
operator runs synthetic .NET tests, private *new* full-proxy
enriched manifest export, then aggregate-only preflight.
No held-out label access, new models, ANN, collection
changes or production suppression. WI-0195 blocked.

**WI-0194 checkpoint 5 (2026-10-19):** full-proxy archive exact
dHash is technically viable offline. Maintainer ran the private
candidate preflight with 4/4 synthetic tests passing: 27,050
photos, 163,029 candidate/control pairs, 25,107 strict
leakage families. The current dHash<=4 split safely groups known
strict links (zero crossings) but has 882 dHash5–8, 3,867
dHash9–12 and 1,596 high-CLIP candidate links crossing split.
Both partitions lack exact-copy controls; held-out is short
41 borderline and 25 screened negative cases. No-go for
review freeze. This checkpoint adds an aggregate-only, read-only
dHash<=4/8/12 family-partition sensitivity analysis to assess
unlinked risk and sample quota viability. It does not alter the
existing reviewer sampler, fit cutoffs, access held-out
review labels, or modify collection behavior. After merge,
operator runs synthetic tests and locally returns only aggregate
stdout. WI-0195 blocked. All new PRs target main.

**WI-0194 checkpoint 4 (2026-10-19):** Maintainer accepted the
full 27,050-proxy technical feasibility run: 365,837,725 dHash
comparisons in 25.91s, CLIP 100 seeded scans across indexed
27,050 vectors in 4.51s, verification/hash 218.89s. 162,829
retrieved candidates, 2,495 strict dHash<=4 links, 737
CLIP-only candidate pairs. No ANN needed to study exact offline
dHash at this size, but CLIP has only 100 seeds. This checkpoint
adds aggregate-only no-output `redundancy_candidate_preflight.py`
to assess leakage-family sizes, cross-partition near-copy warnings
and proposed sampler quota feasibility on the existing private
full-proxy candidate JSON. Operator runs synthetic tests and
the preflight *after merge*, shares only aggregate stdout.
No frozen review set, labels, threshold, quality held-out reuse,
model inference, production collection changes. WI-0195 blocked.
All new PRs target main.

**WI-0194 checkpoint 3 (2026-10-19):** Maintainer's 500, 2000,
and 5000-photo exact dHash/CLIP runs passed. The 5000 run found
27,050 current proxy-backed photos, complete CLIP coverage in the
sample, 12,497,500 hash comparisons in 4.8 s, 499,900 seeded
CLIP comparisons in 0.82 s, and 29,635 candidate pairs. This
checkpoint adds an explicit guarded read-only exact survey of every
current proxy-backed photo (limit 30k photos, 450m comparisons,
16 GiB declared local proxy bytes), aggregate signal-overlap counts,
and fast deterministic top-k. After PR merge maintainer performs
ONE local all-proxy probe with newly named private outputs, returns
only aggregate metrics. Existing CLIP indexed vectors are reused,
not inferred; 100 seeded CLIP searches are not all-pair CLIP recall.
No review labels, held-out reuse, new model inference, ANN,
collection selection changes or suppression. WI-0195 blocked.
All new PRs target main.

**WI-0194 checkpoint 2 (2026-10-19):** PR #587 (checkpoint 1)
merged to main; WI-0193 is already archived. The current work
adds a bounded, strictly offline/local-proxy CLI to extract dHash
candidate neighbours and optionally reuse exact-version CLIP
semantic-search vectors from the existing PostgreSQL catalogue,
via a read-only transaction. The default deterministic subset
is 500 current proxy-backed photos, with an explicit cap of 5000.
It measures exact within-subset hash pairs and seeded CLIP-to-sample
comparisons, exports private candidate pairs plus aggregate metrics,
and makes no archive-wide retrieval or suppression claim. Operator
must run the local probe and return aggregate-only numbers before
scaling, model comparisons, or blinded redundancy review. No
WI-0193 held-out reuse; no private review exports or app changes.
M37 remains in progress; WI-0195 stays proposed. All new PRs
target main.

**WI-0193 checkpoint 18 (2026-10-18): ONE-TIME HELD-OUT RESEARCH
EVALUATION CONSUMED.** Maintainer separately authorized and ran
`heldout_ranking_one_shot.py --mode evaluate` once after merged
PR #583, using TOPIQ NR `pyiqa-topiq_nr-0.1.16`,
`jpeg-1600-q78` proxy and frozen gap
`0.16755089163780212`. 160 representative held-out
photos; 157 known-tier, three unsure; zero score failures.
All three PREDECLARED **point-estimate** research checks passed:
selected different-tier ordering **2236/2581=86.63%** (photo
bootstrap 95% **75.07–93.69%**); all-pair coverage
**4157/12720=32.68%** (CI **26.50–37.85%**);
selected >=2-tier ordering **740/760=97.37%** (CI
**89.92–99.81%**). 8,563 abstentions; 1,438 same-tier
selected; 3 unsure ratings. **Overall ordering interval
crosses BELOW the 80% target**: descriptive held-out pass is
not high-confidence evidence of generalizable >=80%. 1,000
seeded photo-level bootstrap resamples; pairs not independent.

Bounded research **GO** for optional selective pairwise preference
concept only; **NO-GO** for previous absolute three-band maps,
automatic Very Low exclusions, and production ranking/inference
deployment. No model weight/license approval. This held-out
cohort has been used ONCE: **NEVER rerun**, remove the
private one-shot claim, fit thresholds, test additional
candidates on these labels, or change decision post-hoc.
Retain private evidence locally. Next: merge documentation-only
checkpoint-18 outcome PR; conclude WI-0193 with precise
research-go / absolute-classification-no-go decision, and
plan any further optional product/rights evaluation via
separate workitems and independent evidence.

**WI-0193 checkpoint 17 (2026-10-18):** Maintainer completed
private checkpoint-16 TOPIQ p75 freeze attestation. Aggregate local
output confirms exact fixed gap `0.16755089163780212`,
`researchHypothesisFrozen=true`, and **held-out evaluation still
not authorized**. New `heldout_ranking_one_shot.py` implements
safe **preflight** (calibration/score/freeze fingerprints and frozen
160 representative held-out membership only; never touches
held-out/challenge human ratings), plus a separately gated
one-shot evaluator for a **later** explicit maintainer approval.
The evaluator checks a strict private authorization tied to
preflight fingerprint and exclusively writes a one-use claim
before reading held-out quality labels. Synthetic tests cover
abstention/same tiers/photo-level bootstrap/claim reuse.
Research endpoints remain fixed 80%/20%/80%, no held-out
retuning or new candidate. **Next after PR merge:** run _only_
the preflight command documented in the benchmark operations
runbook and share aggregate output. Do not generate separate
authorization or run evaluation yet. No production quality/rank
or automatic Very Low exclusion permitted. M37 PRs target
`features/m37`.

**WI-0193 checkpoint 16 (2026-10-18):** Maintainer explicitly approved
a **research-only TOPIQ NR p75 ranking freeze** after checkpoint 15's
aggregate-only local proposal: candidate `pyiqa-topiq_nr-0.1.16`,
`jpeg-1600-q78` review proxies, higher-is-better, exact fixed
absolute raw-score gap `0.16755089163780212` from 238 calibration
photos (28,203 pairs). Prefer higher score only at positive gap >=
that threshold; otherwise abstain. Frozen research pass criteria:

> =80% correct selected different-tier pairs, >=20% all-pair
> decision coverage, >=80% correct selected >=2-tier pairs; require
> nonzero qualifying denominators and report photo-resampled
> uncertainty and same-tier choices. Candidate/quantile selection
> was exploratory; no held-out tuning or alternative candidates.

The new `ranking_freeze.py` **local-only** attestation verifies the
unchanged private checkpoint-15 proposal and complete calibration
score evidence before writing a new private frozen record.
Maintainer must run it locally after this checkpoint PR merges.
**No held-out evaluator or access authorized yet**; held-out/challenge
human reviews remain sealed. No PyTorch inference or production
model, ranking, automatic exclusion, checkpoint-mapping or license
approval. Future evaluator requires separate code review and explicit
maintainer run authorization. See WI-0193 and benchmark runbook.
M37 PRs target `features/m37`.

**WI-0193 checkpoint 15 (2026-10-18):** Maintainer ran five-fold
within-calibration selective TOPIQ/MUSIQ comparison on 238 photo
ratings, 5635 within-fold pairs. At 75th-percentile score gap,
TOPIQ orders **90.27%** of _different-human-tier selected pairs_
correctly, expressing a preference on **25.57% of all pairs**;
MUSIQ **89.75%** at ~25.08% all-pair coverage. For >=2-tier
human differences: TOPIQ **92.71%** of 247 selected pairs, MUSIQ
**89.49%** of 276. At 90th percentile TOPIQ ~97.4% correct
among different-tier comparisons with only 10.65% all-pair
coverage. Pairs share photos and near-duplicates may cross folds;
this is NOT independent held-out acceptance or absolute-tier
accuracy. Both earlier midpoint absolute classifiers rejected.

Prepare one **TOPIQ NR p75 ranking-only freeze PROPOSAL** using
`ranking_freeze_proposal.py` on existing complete 600-proxy
export. It derives one fixed score-gap threshold from all 238
known-tier calibration photos without looking at held-out labels.
The candidate choice is post hoc to calibration exploration,
not preregistered independent model selection. Proposal explicitly
marks research freeze/held-out permission/production model/Very
Low exclusion all FALSE. Operator runs it locally and shares
aggregate proposal; explicit next-stage review is required before
any one-time held-out evaluation. PyIQA framework and checkpoint
licensing remain unverified; no app ranking changes.

**WI-0193 checkpoint 14 (2026-10-18) calibration diagnostic:** PR #579
merged; maintainer three-band confusion results show TOPIQ NR
37.4% accuracy/49.9% balanced accuracy/23.4% Normal recall;
MUSIQ 37.8%/48.5%/26.0%, versus constant Normal 64.7%
accuracy without ranking. Both predict High-or-above for 102/238
photos with just 25 (TOPIQ) / 21 (MUSIQ) truly High-or-above;
reject both median-midpoint **absolute classification** drafts,
not necessarily the learned model raw-score **ranking** signal.
The new `calibration_selective_ranking.py` is an offline-only,
5-fold within-calibration score-gap abstention diagnostic on the
already-scored TOPIQ/MUSIQ proxy exports. Predeclared 0/50/75/90%
train-only gap quantile cutoffs, validation within-fold photo
pairs; reports overall decision coverage, different-tier and

> =2-tier ordering accuracy. Exact content duplicates in one fold;
> near duplicate and pair-independence limitations noted. NO
> held-out/challenge human labels inspected and no IQA inference,
> production ranker, tier mapping, exclusion or checkpoint licensing
> approved. Next: request aggregate local output and decide on
> ranking-only pre-registration or explicit WI-0193 no-go.

WI-0193 checkpoint 13 calibration-draft evidence: Maintainer ran
existing 600-proxy-score TOPIQ/MUSIQ 3-band drafts on 238 reviewed
representative calibration photos. Low/Normal/High anchor n=38/154/42;
true three-band n=41/154/43. TOPIQ midpoint thresholds
0.4976193383/0.5708234310 yielded 89/238=37.39% correct,
MUSIQ 63.07654476/69.06219482 yielded 90/238=37.82%.
The trivial constant-Normal reference yields 154/238=64.71%.
Thus these **specific proposed midpoint absolute-classification maps**
should not be frozen. This does not negate moderate ranking
evidence (~83% two-tier separated pairwise ordering; rho ~0.36).
Added `three_band_calibration_diagnostic.py` to compare the
**already saved private draft confusion matrices only**, reporting
class balance, precision, recall, balanced accuracy, and macro F1.
No model inference, private benchmark/per-photo rows or held-out
human labels are read; guards reject any approval status.
Next decision: ranking-only/no absolute-tier recommendation vs
more conservative, predeclared calibration/abstention strategy vs
explicit model no-go. No automatic Very Low exclusion. Deployment
rights remain unverified.

**WI-0193 checkpoint 12 pending review (2026-10-18).** PR #577
merged. Maintainer-provided local checkpoint cache inventory confirms
pyiqa 0.1.16, torch 2.14.1, TOPIQ CFANet KonIQ SHA 9a73138b...
181196009 B, MUSIQ KonIQ SHA e95806b9... 108610983 B, and
ARNIQA KonIQ **regressor only** SHA af8f127a... 19560 B. Default
TOPIQ/MUSIQ model-source filenames corroborate earlier load logs,
not yet a weight/license attestation. ARNIQA additionally loads a
learned encoder (and may load torchvision ResNet backbone) from
another Torch cache; its small regressor is not complete provenance.
The new checkpoint scanner supports multiple explicitly listed local
cache roots with privacy-preserving unverified role hints. No model
inference. Next user action: run calibration-only
`calibration_policy_draft.py` on TOPIQ and MUSIQ **existing 600-score**
exports, without `--checkpoint-evidence` until mapping is attested;
share aggregate-only output. Drafts do not freeze policy, approve
production or unseal held-out human ratings. Model weight licensing
remains a separate deployment gate. Automatic Very Low exclusion
still no-go. M37 PRs target `features/m37`.

**WI-0193 checkpoint 11 pending review (2026-10-17).** Following merged
PR #575, the fixed evaluation input is `jpeg-1600-q78` proxy, not
full-resolution originals. The four 600-photo proxy exports remain
complete, none requires re-inference. The current PR upgrades
`checkpoint_provenance.py` with local, no-inference checkpoint cache
inventory (basename/hash/bytes, Python/PyTorch/pyIQA versions) and
adds `calibration_policy_draft.py`, a **calibration-only, unsealed,
coarse three-band** numeric score-mapping proposal. Both have offline
privacy/contract tests. The maintainer must verify model-to-file mapping
and individual checkpoint licensing; framework licenses are not
automatic checkpoint licenses. Every draft marks held-out
evaluation, production deployment and Very Low automatic exclusion
as **disallowed**. Next: collect local provenance and draft aggregates;
explicitly review/freeze or reject a candidate and policy before
one-time held-out human-label evaluation. No held-out/challenge ratings
are read; do not commit private manifest, score exports or paths.
M37 PRs target `features/m37`.

**WI-0193 checkpoint 10: proxy evaluation input decision (2026-10-17).**
The completed metadata-only triage checked all 80 selected originals:
all 17 failures were approximately 49–50 MP (13 RAW, 4 JPEG), whereas
all 63 successful original-pixel pairs were at most 24.47 MP.
No sample exists between 24.47 and 48.77 MP, and the legacy error
code cannot distinguish decoding from inference. The incomplete 63/80,
3-hour high-memory original study is therefore not acceptance evidence.
For M37, use only immutable `jpeg-1600-q78` review proxies as model
inputs (including one-shot held-out); the four existing 600-photo
candidate exports have zero failures. No original CPU rerun or remote
Azure T4 deployment is required. A 2560px post-decode input cap does
not guarantee low RAW decode memory. Next: model weight SHA/size,
license and checkpoint-to-model binding, frozen calibration-only mapping
or no-go, one-time held-out evaluation. Very Low auto exclusion remains
no-go. Keep existing private files and held-out human labels untouched.

**WI-0193 checkpoint 9 (2026-10-17) pending review:** the first TOPIQ
original/proxy CPU experiment had 17/80 failures, notably RAW=13/19
and JPEG=4/21, versus HEIC=0/20 and PNG=0/20. Legacy generic errors
cannot attribute stage, so do not claim RAW decoder failure without
isolating cause. Original/proxy rho=0.624 and top-quartile overlap=25%
are based on 63 successes and are not production acceptance.
The new `original_proxy_metadata_triage.py` verifies the original
SHA and full frozen selection/score manifest before checking metadata
headers of **only the 17 prior failures** (opt-in all 80). It prints
aggregate image-family sizes/header availability without full decoding,
IQA inference, human labels or held-out photos. Local run pending.
No unbounded CPU retry or premature production model selection;
ADR-0011 caption thumbnail authorization does not permit remote IQA
original-pixel uploads. M37 PRs target `features/m37`.

**WI-0193 original/proxy field evidence (2026-10-17):** the initial
unbounded 80-photo TOPIQ CPU run finished the loop but had **63 successes /
17 generic decode-or-inference failures** under ~3 hours of severe RAM
and disk pressure; among successes JPEG=17, HEIC=20, PNG=20, RAW=6.
Original-vs-proxy Spearman 0.624, median shift +0.104, top-quartile
overlap 0.25. These are incomplete/confounded results, **not** a final
evaluation. MUSIQ was interrupted during transformer inference with
no completed export. The TOPIQ private result was written relative to
the repo-root working directory, while later file checking used the
Windows profile working directory. The follow-up report now provides
per-format failures on existing JSON and stage-coded failures in new
runs. No unbounded retry requested. Azure T4 caption Ollama is not an
IQA service and original-pixel upload is not authorized under
caption-only ADR-0011. Preserve 600-photo private benchmark, calibrated
exports and untouched held-out labels. No production model or exclusion
decision.

**WI-0193 checkpoint 7 pending review (2026-10-17):** The maintainer
completed blinded reviews of the frozen 600-photo benchmark and successfully
reconciled the four full candidate exports (all with zero inference
failures). Private-manifest inspection confirmed 240/240 calibration and
200/200 challenge ratings recorded, including the 20 TOPIQ/MUSIQ
disagreement cases; **no held-out quality-rating values were inspected**.
Only 238 calibration ratings are known-tier. The candidate audit retains
MUSIQ rho=0.370, TOPIQ rho=0.359, ARNIQA rho=0.274, WI-0182 rho=0.234.
Automatic Very Low exclusion remains unsupported and no model is chosen.

The new `original_proxy_study.py` supports private deterministic
50–100 image paired original-decode vs immutable proxy scoring using the
TOPIQ/MUSIQ runner, SHA verification, format-balanced selection from
**non-held-out** partitions, and aggregate report. Optional `pillow-heif`
and `rawpy` dependencies are local only. Document codec/color/rendering
differences from production and _do not_ equate the study with pure
downscale effects. Maintainer run and checkpoint provenance/license audit
pending. Next freeze an explicit policy before a one-shot held-out analysis.
M37 PRs target `features/m37`, not main.

**WI-0193 checkpoint 6 pending review:** `reconcile_quality_scores.py` now
extends the frozen 580-photo learned-IQA score exports to 600 **only after**
the extra 20 rank-disagreement challenge photos are imported. It requires an
explicit 580-photo pre-import snapshot, validates unchanged frozen
representatives, benchmark identity and exact imported candidates. TOPIQ/MUSIQ
reuse the earlier separately scored screening-pool rows; ARNIQA can score only
the 20 selected proxies; the WI-0182 baseline is cheap to rerun on 600.
All old score exports remain unchanged and new private 600-photo exports have
strict model version/provider/coverage checks. See
`docs/operations/photo-quality-benchmark.md` checkpoint 6 for the ordered
PowerShell procedure, especially the mandatory separate pre-import backup:
the legacy `.before-challenge.bak` may already exist from WI-0192.
Maintainer archive screening/import and score reconciliation are **not yet run**.
No held-out metrics or model/threshold policy freeze. No production changes.

**Checkpoint 5 pending review:** The existing catalogue-backed challenge generator now
supports bounded `--pool-only` screening of fresh, non-benchmark images, excluding
benchmark content SHA and deduplicating pool content. `disagreement_challenges.py`
scores this pool (distinct input kind) with TOPIQ/MUSIQ and selects up to 20
private, blinded candidate imports by within-pool normalized-rank disagreement,
with deterministic tie-breaking. No maintained-archive disagreement candidates
have yet been run/imported/reviewed. Importing 20 expands the benchmark to
600: the four existing 580-photo candidate exports will need explicit
reconciliation against that new benchmark before the final held-out decision.
No automatic exclusion, production scoring, or model selection is enabled.
M37 checkpoint PRs target `features/m37`, not `main`.

**2026-10-17 evidence:** WI-0193 now has full private-archive benchmark score exports
for WI-0182, TOPIQ NR, MUSIQ and ARNIQA (580/580 each, zero failures), with 238
known-tier representative calibration reviews. MUSIQ rho=0.370/pairwise=82.8%;
TOPIQ rho=0.359/pairwise=83.8%; bootstrap differences include zero. Very Low
precision is unproven (only 3 calibration Very Low labels); automatic exclusion
remains **no-go**. The aggregate-only evidence is documented in
`docs/operations/photo-quality-model-evidence-2026-10-17.md`. The added
`paired_calibration_confidence.py` produces uncertainty estimates without
consulting held-out labels. Next gates: weight hashes/licenses, 20 new
non-benchmark disagreements, original-versus-proxy comparison, frozen
calibration, one-shot held-out. No production model selected. M37 PRs target
`features/m37`, not main.

WI-0193 / issue #536 is in progress on the M37 integration branch `features/m37`. Checkpoint 1 establishes a benchmark-bound candidate score schema, a fixed WI-0182 technical-reason-count baseline and calibration-only rank/pairwise/Very-Low safety metrics with Wilson uncertainty. Held-out human labels remain excluded from evaluation until a candidate and calibration policy are frozen. WI-0192 is maintainer-accepted: 400 representative photos (240 calibration / 160 held-out) plus 180 challenge photos from 27,052 durable-proxy candidates, with 20 slots reserved for WI-0193 model-disagreement cases; 255 blinded ratings were completed at WI-0192 acceptance and the sample was judged representative. Legacy original width/height remain a documented limitation. M37 checkpoint PRs target `features/m37`, not `main`.

WI-0182 / issue #490: PR #509 implements the evaluation-only technical-quality harness over durable review proxies. It adds versioned raw sharpness/exposure/clipping/contrast/resolution evidence, q78 profile-gated candidate reasons, deterministic PostgreSQL proxy sampling, private JSON review export, timing/storage measurements and synthetic analyzer coverage. No collection semantics or catalogue quality persistence are introduced. Keep WI-0182 in review until representative private-archive review/runtime evidence is recorded.

WI-0186 / issue #512: PR #515 scopes Visual/Caption/Combined search through an optional saved Smart Collection without weakening exact Smart Collection semantics. The server resolves one eligibility set, applies it before semantic top-k and inside caption SQL, and keeps explicit saved search-result collections immutable. PR #517 corrected the large-scope PostgreSQL timeout. The 2026-10-13 maintainer retest succeeded for Fideli (5,638 eligible) in all three modes and Mor och Dotter (1,057 eligible); provenance badges behave as expected. Keep WI-0186 in review until independent exact-membership and saved-list restart verification is recorded.

WI-0187 / issue #513: PR #516 lets Creative Collections use either the existing exact Smart Collection anchor or a versioned ranked Search anchor with Visual/Caption/Combined mode, optional Smart scope and bounded top-N admission. Search anchor materialization reuses WI-0186 retrieval, then enters the existing one-level moment/context and deterministic Creative selection pipeline. The maintainer now reports the search-anchor workflow works as expected. Keep WI-0187 in review pending the specific two-recipe, legacy compatibility and snapshot-immutability evidence in its acceptance criteria.

M30: WI-0150 completed, ADR-0012 accepted, and WI-0151 in progress. The maintainer's 2026-10-15 pilot verified 25 locally available videos (1,284 online-only untouched), 16 correct SDR posters across H.264/HEVC Main and 9 deliberately deferred HEVC Main 10 HLG clips, zero reported failures and no unexpected hydration. Anonymous evidence is documented in docs/operations/video-pilot-evidence-2026-10-15.md. Checkpoint 2 adds PostgreSQL v33 photo-default media-kind and revision-bound video metadata/poster schema with isolation tests. The CLI remains isolated: no automatic video writes, no gallery cards, and no photo-only query changes yet.

## Maintainer acceptance — 2026-10-04 (Europe/Stockholm)

The maintainer completed the current maintained UI review batch:

- WI-0165 is accepted after PR #505; slideshow counts, player-triggered **Prepared** continuity, reload/revalidation restoration and stale-state behavior work as expected.
- WI-0178 is accepted after PR #501; the maintained desktop and phone/PWA slideshow-library search/filter experience works as expected.
- WI-0185 is accepted after PRs #506, #507 and #508; the date-only audit filters and the remaining cross-person assignment audit workflow work as expected.

WI-0165, WI-0178 and WI-0185 are completed. M35 is completed. Issues #499 and #503 are resolved; issue #481 was already closed by PR #501 and now has maintained acceptance evidence.

The maintainer previously accepted WI-0163, WI-0167, WI-0169, WI-0170, WI-0171, WI-0172, WI-0173, WI-0175, WI-0176, WI-0177, WI-0179, WI-0180, WI-0181, WI-0183 and WI-0184.

## Next concrete step

WI-0193 checkpoint 1 is the current M37 task. After the calibration protocol merges, run the fixed WI-0182 baseline on the private benchmark, then add at least three justified learned no-reference IQA candidates (TOPIQ, MUSIQ, ARNIQA or deployable equivalents), reserve the remaining challenge slots for model disagreements, and compare proxy versus decoded-original input before freezing a calibration policy. WI-0194 remains independently ready.

WI-0151: engineering implementation of the initial opt-in local MOV/MP4 metadata and revision catalogue, exclusion-safe revision-owned Rec.709 SDR still posters, default-off read-only Videos page and mixed-media photo-only isolation is complete; PR #557 CI passed. The final checkpoint adds validated opt-in Windows launcher support and a consolidated maintained-machine acceptance runbook. **Next is the maintainer's requested one-time local test** (read-only baseline, verified backup, bounded video ingest/poster writes, opt-in video UI, photo regressions, privacy/nonhydration review). Keep WI-0151 in_review until evidence is recorded; do not call private HLG candidate images production-ready or enable any automatic video jobs. Only explicitly signalled bt709 input is accepted for durable posters; other HDR/unknown profiles remain deferred.

## Relevant pointers

- docs/delivery/work-items/WI-0186-scoped-semantic-caption-search.md
- docs/operations/semantic-caption-photo-search-evaluation.md
- docs/delivery/work-items/WI-0187-search-creative-anchors.md
- docs/operations/creative-collection-preview.md
- docs/delivery/work-items/WI-0182-photo-quality-scoring-evaluation.md
- docs/operations/photo-quality-evaluation.md
- docs/decisions/ADR-0012-isolated-video-media-architecture.md
- docs/operations/video-codec-survey.md
- docs/operations/video-codec-survey-evidence-2026-10-14.md
- docs/operations/video-pilot-evidence-2026-10-15.md

\nWI-0193 checkpoint 2 adds the private, explicit opt-in Python `pyiqa`
runner for `topiq_nr`, `musiq`, and `arniqa` with SHA-verified
benchmark proxies and schema-compatible scored exports. The runner has offline
contract tests and the maintainer has now run the full private 580-photo benchmark;
exact model checkpoint hashes/licenses, original comparison and held-out decision
remain outstanding. Do not confuse candidate support with
model acceptance.

WI-0193 checkpoint 3 adds `candidate_audit.py` and offline tests for exact full-score
coverage and calibration-only comparative metrics. Full learned-model runs are complete;
weight hash/license validation, original-vs-proxy checks, disagreement challenge sampling
and one-shot held-out evaluation are still outstanding. Do not choose a production model based
on a framework's model name or on an unverified score export.
