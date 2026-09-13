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

## Acceptance criteria

- [ ] WI-0081 is resolved before any production automatic-assignment semantics are broadened.
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
