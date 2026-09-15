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

WI-0081 resolved the earlier matcher-quality concern on 2026-09-15. Its accepted duplicate-resistant baseline measured 96.491% top-1 and 99.844% conservative High precision with 0.121% reviewed-Unknown High emission. The maintainer explicitly chose to keep current max-exemplar ranking and the current High score+margin policy. Centroid and globally capped reference alternatives materially regressed, so WI-0117 must treat those findings as constraints rather than reopening them without new evidence.

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

## Initial private evaluation slice

`tools/cluster-evaluation/evaluate_auto_assignment.py` is the first WI-0117 slice. It is read-only and uses the existing pseudonymized exact-model `sample.json`; it does not write suggestion policy rows or canonical review history.

The evaluator uses the current production reference population exported as `referenceFaces`, but ranks every reviewed target with a duplicate-resistant holdout: the target itself and every reference from the same exact-content group are excluded. The exact persisted High/Medium thresholds from the sample remain the baseline policy.

Policy selection and validation are separated deterministically by exact-content group. `sha256(contentGroup) mod 5` assigns buckets 0-2 to the 60% selection partition and buckets 3-4 to the 40% holdout partition. Keeping whole exact-content groups in one partition prevents an image and its exact copies from leaking between selection and validation.

Candidate rules only expand the current High policy for targets that remain at least Medium under duplicate-resistant ranking. The first grid evaluates three evidence families:

1. **Independent multi-reference support** — require the rank-1 Person to have 2-4 independent exact-content reference groups above support-score gates from 0.50 through 0.65, together with candidate target margin gates from 0.00 through 0.10.
2. **Cluster corroboration** — require the target's rank-1 Person to agree with target-specific Strong DBSCAN cluster evidence. The target itself and all members from its exact-content group are removed from the cluster vote. The accepted WI-0116 support/core/competition thresholds remain fixed.
3. **Cluster plus multi-reference support** — require both Strong target-specific cluster corroboration and independent reference support for the same rank-1 Person.

The selection split classifies candidates before any holdout results are exposed. `strict-no-extra-false` means the expanded policy gains assignments without increasing either wrong-known or reviewed-Unknown assignments versus current High. `rate-preserving` means known precision, conservative precision and reviewed-Unknown assignment rate are no worse than current High. Only candidates in those classes are automatically shortlisted for holdout validation.

The report records absolute and incremental assignment counts, wrong-known assignments, reviewed-Unknown assignments, conservative precision, known precision/coverage and estimated review actions saved. The selection winner is also segmented on the holdout by detector confidence `<0.70`, face area `<0.5%`, and sparse true identities with fewer than ten usable references so WI-0081 quality risks remain visible.

The private export does not contain durable cluster `not same` constraints. The evaluator therefore does not credit them when selecting candidates. Production WI-0116 cluster evidence already fails closed on those constraints, so this omission makes the private candidate simulation more permissive on that dimension rather than hiding a known source of negative evidence.

No production auto-assignment behavior is changed by this slice. If no candidate survives the selection and holdout guardrails, WI-0117 may close with current High-only automation unchanged.

## Acceptance criteria

- [x] WI-0081 is resolved before any production automatic-assignment semantics are broadened.
- [ ] Candidate multi-evidence rules are evaluated on a private reviewed sample with the same deterministic split/quality procedure used for policy selection.
- [ ] Evaluation reports false automatic assignments explicitly and compares them with the current accepted High policy.
- [ ] Unknown-person rejection is measured so increased known-person coverage does not come from silently forcing unknown faces into existing identities.
- [ ] Any accepted broader policy requires independent evidence beyond one marginal face/exemplar match and fails closed on strong competing-person evidence.
- [ ] Automatic decisions retain exact embedding/clustering/policy provenance sufficient for later audit.
- [ ] Fixed-snapshot regeneration and no-same-run-cascade invariants remain intact.
- [ ] Manual reassignment/undo continues to supersede automatic decisions through append-only canonical history.
- [ ] Broader automation is opt-in and disabled by default for a newly introduced policy revision unless explicit acceptance says otherwise.
- [ ] If evaluation does not demonstrate an acceptable precision/review-effort trade-off, the work item closes with existing automation unchanged rather than weakening safeguards.

## Verification requirements

Private reviewed-sample evaluation, automated policy/scoring/audit integration coverage and human Windows verification of opt-in configuration plus correction/undo of a representative automatic decision if broader automation is accepted.
