---
id: WI-0116
title: Add cluster-assisted known-person advisory evidence
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0114, WI-0043]
related_adrs: [ADR-0006]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, PhotoIdentity.Integration.Tests]
---

# WI-0116: Add cluster-assisted known-person advisory evidence

## Objective

Use coherent provisional-cluster support to strengthen advisory known-person suggestions without lowering the existing ordinary per-face High threshold or allowing cluster membership alone to create canonical assignments.

## Why

A group of mutually similar unreviewed faces can provide evidence that is invisible to the current best-single-exemplar person score. For example, many members of one coherent cluster may independently favor the same known person even though no individual member clears the current automatic High gate. Aggregating that agreement can improve review ordering and confidence while remaining separate from automatic assignment.

## In scope

- Define cluster-level advisory evidence for a known person, including member vote/support count, score distribution, competing-person evidence and cluster cohesion/conflict signals.
- Keep the existing per-face rank-1/rank-2 score and confidence policy intact as a separately visible signal.
- Produce model/policy-versioned derived cluster-assisted suggestions that can be regenerated.
- Require sufficient independent support; one strong member must not cause the whole cluster to inherit an identity.
- Detect mixed/ambiguous clusters and fail closed to ordinary per-face review.
- Surface cluster-assisted evidence in relevant review views with clear explanation of why the group is suggested.
- Preserve rejected face-person pairs and cluster negative evidence.
- Measure whether cluster support improves review recall/throughput without materially increasing false-person proposals.

## Out of scope

- Automatic canonical assignment based on cluster-assisted evidence.
- Lowering the existing per-face High threshold.
- Replacing ordinary person-ranking evidence with cluster evidence.
- Changing the canonical Person model.

## Implementation notes

The initial advisory policy is versioned as `m25-cluster-known-person-v1`. It reads only the current provisional cluster for one exact embedding model/hash, the accepted clustering policy, the current ordinary rank-1 suggestion projection for the same exact model, and the ordinary suggestion-policy version. Advisory evidence is recomputed on demand rather than persisted as a second independently stale identity store, so rebuilding either the cluster or ordinary suggestion projection deterministically regenerates the result.

A member casts a qualifying vote only when its current rank-1 suggestion is `pending` and meets the existing ordinary Medium score threshold. The ordinary High threshold and margin rule are not lowered or rewritten. Independence is defined by exact source-content hash: duplicate copies/revisions of the same underlying photo share one evidence group, and each evidence group can cast at most one qualifying vote total. When multiple faces in the same exact-content group qualify, the strongest deterministic rank-1 row represents that group. The initial conservative rule requires at least 3 independent exact-content qualifying votes for one Person, at least 60% support across independent content groups, and at least 60% Core membership. A competing Person with more than 1 qualifying independent-content vote or more than 20% support makes the result `ambiguous`. Any explicit current internal `not same` conflict also fails closed to `ambiguous`. Results otherwise remain `insufficient` rather than inheriting an identity from one strong member.

The PostgreSQL projection explicitly excludes faces with an active canonical Assign/Unknown/Reject action, reads only the requested exact model/hash, derives the independent evidence group from the source revision content hash, and does not resurrect rejected face-person pairs. The API returns exact-model, clustering-policy, advisory-policy and ordinary suggestion-policy provenance together with independent support counts, score distribution, competing evidence and an explanation. `CanonicalAssignmentAllowed` is always false in this work item.

`People to identify` displays this evidence in a separate advisory panel when a provisional group is opened. It labels support and coverage as independent exact-content evidence rather than raw face counts. It does not preselect the suggested Person, select faces, or alter the existing audited bulk-review action. Ordinary per-face ranking remains independent and visible through the existing review surfaces. After a canonical assignment, advisory evidence is re-read so reviewed members no longer contribute current rank-1 votes.

Private evaluation reuses the pseudonymized WI-0113 exact-model export. Export schema v2 adds a pseudonymized exact-content group so `tools/cluster-evaluation/evaluate_advisory.py` applies the same duplicate-resistant independence rule as production. The evaluator fixes clustering to the production DBSCAN policy, simulates per-face known-person ranking without self-matching, and reports conservative proposal precision, false-person proposals, recall over pure reviewed opportunities, mixed-cluster Strong outcomes and estimated review-task compression. Private samples/reports remain uncommitted.

## Acceptance criteria

- [ ] Cluster-assisted identity evidence has explicit exact-model/clustering-policy provenance and is regenerable.
- [ ] A cluster can receive advisory support for a known person only when multiple independent exact-content groups provide qualifying evidence under the accepted rule; duplicate copies cannot inflate support.
- [ ] Competing-person support or internal cluster conflict prevents a high cluster-assisted confidence classification according to explicit rules.
- [ ] Existing per-face rank/score/margin evidence remains available and is not overwritten by cluster-level evidence.
- [ ] Rejected face-person pairs and recorded cluster conflicts are respected.
- [ ] The UI explains cluster-assisted support as advisory evidence rather than a confirmed identity.
- [ ] No cluster-assisted result creates a canonical assignment in this work item.
- [ ] Private reviewed evaluation reports precision/recall/review-effort impact relative to the current per-face suggestion workflow.
- [ ] Automated coverage protects mixed-cluster fail-closed behavior, duplicate-resistant support, and exact-model/policy isolation.

## Verification requirements

Automated scoring/persistence/API coverage plus private reviewed-sample evaluation and human review verification of coherent, ambiguous and intentionally mixed clusters.

For maintainer acceptance, run the private evaluator using the production exact-model sample, retain the report outside the repository, and record only aggregate/non-personal findings. In the web application inspect representative `Strong`, `Ambiguous` and `Insufficient` groups when available. Confirm the advisory panel never changes Person selection or review state by itself, support counts are described as independent exact-content evidence, ordinary per-face evidence is still available, and the WI-0115 card-based anchor selection remains usable while reviewing the same group.
