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

## Acceptance criteria

- [ ] Cluster-assisted identity evidence has explicit exact-model/clustering-policy provenance and is regenerable.
- [ ] A cluster can receive advisory support for a known person only when multiple independent members provide qualifying evidence under the accepted rule.
- [ ] Competing-person support or internal cluster conflict prevents a high cluster-assisted confidence classification according to explicit rules.
- [ ] Existing per-face rank/score/margin evidence remains available and is not overwritten by cluster-level evidence.
- [ ] Rejected face-person pairs and recorded cluster conflicts are respected.
- [ ] The UI explains cluster-assisted support as advisory evidence rather than a confirmed identity.
- [ ] No cluster-assisted result creates a canonical assignment in this work item.
- [ ] Private reviewed evaluation reports precision/recall/review-effort impact relative to the current per-face suggestion workflow.
- [ ] Automated coverage protects mixed-cluster fail-closed behavior and exact-model/policy isolation.

## Verification requirements

Automated scoring/persistence/API coverage plus private reviewed-sample evaluation and human review verification of coherent, ambiguous and intentionally mixed clusters.
