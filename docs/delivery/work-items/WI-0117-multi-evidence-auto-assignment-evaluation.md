---
id: WI-0117
title: Evaluate multi-evidence automatic identity assignment
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0081, WI-0116]
related_adrs: [ADR-0006]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0117: Evaluate multi-evidence automatic identity assignment

## Objective

Determine whether multi-exemplar and cluster-level evidence can safely expand opt-in automatic identity assignment beyond the current single-face High rule while preserving measured precision, unknown rejection, auditability and reversible canonical history.

## Why

The current matcher effectively uses the best single exemplar per person plus rank-1/rank-2 margin. That is intentionally conservative, but it can miss repeated faces where several weaker independent signals agree. M25 may provide stronger evidence through multiple exemplars and coherent provisional clusters; this evidence must be evaluated separately before it is allowed to create canonical assignments.

WI-0081 resolved the earlier matcher-quality concern on 2026-09-15. Its accepted duplicate-resistant baseline measured 96.491% top-1 and 99.844% conservative High precision with 0.121% reviewed-Unknown High emission. The maintainer explicitly chose to keep current max-exemplar ranking and the current High score+margin policy. Centroid and globally capped reference alternatives materially regressed, so WI-0117 treats those findings as constraints rather than reopening them without new evidence.

## In scope

- Start only after WI-0081 has resolved the existing suggestion-accuracy concern and WI-0116 has produced stable advisory cluster-assisted evidence.
- Define candidate multi-evidence features such as best score, top-k exemplar aggregate, number of supporting exemplars, person-centroid similarity where justified, competing-person support and cluster-level agreement/cohesion.
- Evaluate candidate rules on a private reviewed sample using deterministic splits and exact-model provenance.
- Report person-identification precision, false assignment rate, unknown rejection, coverage/recall and expected review-effort reduction.
- Compare against the current accepted per-face High automatic-assignment policy rather than only against a lower threshold baseline.
- Select an explicit fail-closed policy if broader automation is justified; otherwise document that existing automatic assignment remains unchanged.
- If enabled, persist a separately versioned policy with exact evidence/provenance sufficient to explain each automatic canonical decision.
- Preserve fixed-snapshot/no-same-run-cascade semantics and manual supersession/undo behavior.
- Add an operator opt-in toggle and safe defaults; new policy revisions must not silently retroactively rewrite historical assignments.

## Out of scope

- Enabling broader automatic assignment without reviewed evaluation evidence.
- Automatically creating canonical people from unnamed provisional clusters.
- Treating cluster membership alone as sufficient identity proof.
- Hiding model/policy provenance from canonical automatic decisions.
- Replacing the accepted max-exemplar matcher with the WI-0081 centroid or cap-8 reference strategies that already regressed materially.
- Lowering the global High threshold merely to increase automatic-assignment coverage.

## Private evaluation procedure

`tools/cluster-evaluation/evaluate_auto_assignment.py` is read-only and uses the existing pseudonymized exact-model `sample.json`; it does not write suggestion policy rows or canonical review history.

The evaluator uses the current production reference population exported as `referenceFaces`, but ranks every reviewed target with a duplicate-resistant holdout: the target itself and every reference from the same exact-content group are excluded. The exact persisted High/Medium thresholds from the sample remain the baseline policy.

Policy selection and validation are separated deterministically by exact-content group. `sha256(contentGroup) mod 5` assigns buckets 0-2 to the 60% selection partition and buckets 3-4 to the 40% holdout partition. Keeping whole exact-content groups in one partition prevents an image and its exact copies from leaking between selection and validation. DBSCAN cluster evidence is constructed independently inside each partition so holdout cluster structure cannot influence policy selection.

Candidate rules only expand the current High policy for targets that remain at least Medium under duplicate-resistant ranking. The grid evaluates three evidence families:

1. **Independent multi-reference support** — require the rank-1 Person to have 2-4 independent exact-content reference groups above support-score gates from 0.50 through 0.65, together with candidate target margin gates from 0.00 through 0.10.
2. **Cluster corroboration** — require the target's rank-1 Person to agree with target-specific Strong DBSCAN cluster evidence. The target itself and all members from its exact-content group are removed from the cluster vote. The accepted WI-0116 support/core/competition thresholds remain fixed.
3. **Cluster plus multi-reference support** — require both Strong target-specific cluster corroboration and independent reference support for the same rank-1 Person.

The selection split classifies candidates before any holdout results are exposed. `strict-no-extra-false` means the expanded policy gains assignments without increasing either wrong-known or reviewed-Unknown assignments versus current High. `rate-preserving` means known precision, conservative precision and reviewed-Unknown assignment rate are no worse than current High. Only candidates in those classes are automatically shortlisted for holdout validation.

The private export does not contain durable cluster `not same` constraints. The evaluator therefore does not credit them when selecting candidates. Production WI-0116 cluster evidence already fails closed on those constraints, so this omission makes the private candidate simulation more permissive on that dimension rather than hiding a known source of negative evidence.

## Private evaluation findings — 2026-09-15

The maintainer evaluated 14,469 reviewed exact-model targets against 10,353 confirmed production references across 160 identities. The deterministic exact-content split produced 8,722 selection targets and 5,747 untouched holdout targets.

Current High baseline:

- Selection: 3,142 automatic assignments, 5 conservative false assignments, **99.841%** conservative precision, **50.434%** known coverage and 2 reviewed-Unknown assignments (**0.080%** of Unknown targets).
- Holdout: 1,992 assignments, 4 conservative false assignments, **99.799%** conservative precision, **48.195%** known coverage and 3 reviewed-Unknown assignments (**0.185%**).
- Whole sample: 5,134 assignments, 9 conservative false assignments, **99.825%** conservative precision, **49.541%** known coverage and 5 reviewed-Unknown assignments (**0.121%**).

Multi-reference support by itself was rejected. The broad `multi-ref-2x-0.50-margin-0.00` rule added 2,984 selection assignments but also added 71 wrong-known and 305 reviewed-Unknown assignments; even adding a 0.05 margin still produced 20 wrong-known and 111 reviewed-Unknown additions. Independent references therefore help only when combined with stronger corroborating evidence.

Cluster-plus-reference rules were materially different. Every shortlisted `cluster-plus` candidate preserved the strict selection guardrail. The evaluated `cluster-plus-2x-0.50-margin-0.05` candidate added 72 selection assignments, all 72 correct known assignments, with **0 additional wrong-known and 0 additional reviewed-Unknown assignments**.

On untouched holdout, the same candidate added **50 assignments, all 50 correct known**, with **0 additional wrong-known and 0 additional reviewed-Unknown assignments**. Total holdout conservative false assignments therefore remained 4 while assigned count rose from 1,992 to 2,042; conservative precision moved from 99.799% to 99.804%, and known coverage rose from **48.195% to 49.406% (+1.211 percentage points)**.

The holdout quality segments did not expose a new error caused by the expansion: detector-confidence `<0.70` gained 6 assignments without increasing its one baseline false assignment; face-area `<0.5%` gained 9 without increasing its three baseline false assignments; sparse identities with fewer than ten usable references gained no assignments. This policy therefore improves coverage chiefly where repeated cluster/reference evidence exists rather than trying to force sparse identities.

The private split produced 266 partition-isolated clusters and 8,931 noise targets. 2,607 targets had target-specific Strong cluster corroboration before agreement with the target's own rank-1 Person was required. Target-specific votes excluded the target itself and its same exact-content group.

### Selected production candidate

The production candidate is **`m25-multi-evidence-auto-v1`**, corresponding to the holdout-validated `cluster-plus-2x-0.50-margin-0.05` rule:

- keep the existing High automatic-assignment path unchanged;
- consider only rank-1 targets that are at least current Medium and at least score 0.50;
- require rank-1/rank-2 margin **>= 0.05**;
- require at least **2 independent exact-content confirmed references** for the same rank-1 Person with cosine similarity **>= 0.50**, excluding references from the target's exact-content group;
- require target-specific **Strong `m25-cluster-known-person-v1` corroboration** for that same Person from a fresh completed `m25-dbscan-v1` cluster run with `includeUnknown=true`;
- exclude the target itself and same exact-content members from cluster votes;
- preserve the WI-0116 support, core-share and competing-person fail-closed gates;
- any current internal `not same` evidence makes the cluster evidence fail closed;
- if the required cluster evidence is missing, stale or not complete, perform no multi-evidence expansion;
- the new policy is exact-model scoped, separately versioned, opt-in and **disabled by default**;
- the existing ordinary auto-assignment toggle remains the master switch: disabling ordinary automatic assignment disables the multi-evidence expansion too.

All candidates are fully evaluated from the fixed regeneration/reference/cluster evidence before any canonical accept is applied, so one newly automatic assignment cannot strengthen another candidate inside the same run. Automatic decisions use the normal suggestion-acceptance/canonical review-history boundary, with a distinct actor and a provenance note containing exact model, ordinary policy version, multi-evidence policy/configuration version, cluster run/policy/key, cluster support/core/competition values, reference-support count/threshold, target score and margin.

## Acceptance criteria

- [x] WI-0081 is resolved before any production automatic-assignment semantics are broadened.
- [x] Candidate multi-evidence rules are evaluated on a private reviewed sample with the same deterministic split/quality procedure used for policy selection.
- [x] Evaluation reports false automatic assignments explicitly and compares them with the current accepted High policy.
- [x] Unknown-person rejection is measured so increased known-person coverage does not come from silently forcing unknown faces into existing identities.
- [x] Any accepted broader policy requires independent evidence beyond one marginal face/exemplar match and fails closed on strong competing-person evidence.
- [ ] Automatic decisions retain exact embedding/clustering/policy provenance sufficient for later audit. Production implementation and verification pending.
- [ ] Fixed-snapshot regeneration and no-same-run-cascade invariants remain intact. Production implementation and verification pending.
- [ ] Manual reassignment/undo continues to supersede automatic decisions through append-only canonical history. Production verification pending.
- [ ] Broader automation is opt-in and disabled by default for a newly introduced policy revision unless explicit acceptance says otherwise. Production implementation and verification pending.
- [x] Evaluation demonstrates an acceptable precision/review-effort trade-off for a narrow candidate; unsafe multi-reference-only candidates are explicitly rejected rather than weakening safeguards.

## Verification requirements

Private reviewed-sample evaluation, automated policy/scoring/audit integration coverage and human Windows verification of opt-in configuration plus correction/undo of a representative automatic decision if broader automation is accepted.
