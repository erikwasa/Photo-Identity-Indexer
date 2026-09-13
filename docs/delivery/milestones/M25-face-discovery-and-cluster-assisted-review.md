---
id: M25
title: Face discovery and cluster-assisted identity review
status_source: ../status/milestones.yaml
depends_on: [M17, M24]
---

# M25: Face discovery and cluster-assisted identity review

## Outcome

Photo Identity reduces large unreviewed-face backlogs by helping the operator discover repeated unknown people, find faces similar to a chosen face, review likely matches in coherent groups and feed new human identity evidence back into matching without lowering the existing precision-oriented single-face auto-assignment gate.

The milestone introduces provisional face clusters as model-versioned, regenerable evidence. A cluster means that a set of faces is likely to depict the same person; it is not itself a canonical Person and does not create a canonical assignment. Human review remains authoritative unless a later explicitly evaluated policy qualifies for opt-in automatic assignment under the existing ADR-0006 provenance and audit rules.

## Delivery principles

- Reduce the number of operator decisions, not merely the number of clicks per face.
- Prefer false splits over false merges: seeing one person in two provisional clusters is safer than silently combining two people.
- Keep clustering and nearest-neighbour discovery derived and exact-model scoped; canonical people and review history remain model-independent.
- Do not lower the current single-face High threshold simply to increase recall.
- Separate unknown-person discovery from known-person identity matching. They may exchange evidence, but one must not silently redefine the other.
- Treat manual "not the same person" and rejected face-person evidence as durable negative evidence where practical.
- Keep mobile review viable by presenting representative groups and exception handling rather than requiring long per-face queues.
- Use the PostgreSQL catalogue and bounded background-work patterns established by M24; pgvector/ANN may be introduced when measured catalogue scale justifies it, but the contract must not depend on one index implementation.
- Do not allow a newly derived cluster to become a canonical Person automatically.
- WI-0081 remains the prerequisite for changing production identity-scoring or automatic-assignment semantics; M25 discovery work must not mask an unresolved matching-quality regression.

## Work items

- [WI-0110](../work-items/WI-0110-similar-face-explorer.md) - add a face-to-face similarity explorer so one discovered face can immediately surface likely repetitions for bulk review.
- [WI-0111](../work-items/WI-0111-event-driven-match-regeneration.md) - coalesce identity-evidence changes into bounded follow-up regeneration so useful human assignments do not require a separate manual maintenance cycle.
- [WI-0112](../work-items/WI-0112-suggested-person-review-workspace.md) - add a suggested-person-oriented review workspace for processing existing ranked suggestions in coherent groups.
- [WI-0113](../work-items/WI-0113-provisional-cluster-model-and-algorithm-evaluation.md) - define the provisional cluster contract and evaluate DBSCAN/HDBSCAN/conservative graph approaches on reviewed data before selecting production semantics.
- [WI-0114](../work-items/WI-0114-scalable-incremental-face-clustering.md) - implement bounded, regenerable and incrementally maintainable face clustering using the accepted algorithm and model-version boundary.
- [WI-0115](../work-items/WI-0115-cluster-discovery-review-workspace.md) - build the People-to-identify cluster review workflow with representative faces, bulk assignment and exception handling suitable for desktop and mobile.
- [WI-0116](../work-items/WI-0116-cluster-assisted-known-person-evidence.md) - combine coherent cluster support with known-person evidence to produce stronger advisory suggestions without weakening the ordinary per-face threshold.
- [WI-0117](../work-items/WI-0117-multi-evidence-auto-assignment-evaluation.md) - evaluate whether multi-exemplar and cluster-level evidence can safely expand opt-in automatic assignment while preserving measured precision and auditability.

## Delivery sequence

1. WI-0110 provides the shortest path from one recognised unknown face to its likely repetitions.
2. WI-0111 removes the manual regenerate step after useful identity evidence changes, while preserving durable stale-evidence and fixed-snapshot semantics.
3. WI-0112 makes the existing known-person suggestion stream faster to process even before clustering exists.
4. WI-0113 establishes the derived cluster contract and selects a conservative clustering method from measured evidence rather than assumption.
5. WI-0114 implements the selected clustering method with bounded PostgreSQL-scale execution and incremental maintenance for newly analysed faces.
6. WI-0115 turns clusters into the primary unknown-person discovery/review workflow.
7. WI-0116 adds cluster-level support as advisory evidence for known-person matching.
8. WI-0117 is an explicit quality gate for any broader automatic assignment. It may conclude that automation should remain unchanged.

WI-0110, WI-0111, WI-0112 and WI-0113 can proceed independently when their dependencies are satisfied. Later clustering work must not block delivery of the simpler review improvements.

## Exit criteria

- [ ] From any eligible unreviewed face, the operator can open a similarity-ranked view of likely repetitions and bulk-assign selected matches without waiting to encounter them in normal queue order.
- [ ] Useful human identity changes can trigger/coalesce a later regeneration without requiring the operator to remember a separate manual step, while fixed-snapshot and stale-evidence guarantees remain intact.
- [ ] Existing ranked known-person suggestions can be reviewed by person/group rather than only as an undifferentiated face stream.
- [ ] A model-versioned provisional cluster representation exists and is explicitly non-canonical.
- [ ] The selected clustering method has measured split/merge/noise behavior on a reviewed sample, with false-merge risk treated as the primary safety metric.
- [ ] Large unreviewed populations can be clustered in bounded/restart-safe work without requiring all pairwise face comparisons in application memory.
- [ ] The operator can review a cluster through representative faces, inspect members, remove exceptions and assign/create a person from desktop and mobile.
- [ ] New analysed faces can join existing discovery work without requiring a destructive full reset of canonical identity state.
- [ ] Unknown faces may be intentionally rediscovered/rematched without silently changing their canonical Unknown state.
- [ ] Cluster support may strengthen advisory known-person suggestions, but it cannot by itself create a canonical assignment before WI-0117 acceptance.
- [ ] Any broader automatic-assignment rule is enabled only after WI-0081 is resolved and private reviewed evaluation demonstrates an acceptable precision/unknown-rejection trade-off; otherwise existing automation remains unchanged.

## Risks

- Density clustering can bridge two visually similar people through marginal faces; conservative core membership and explicit exception/rejection handling are required.
- One global distance threshold may split or merge identities differently across age, pose, image quality and face size; algorithm evaluation must include those conditions.
- Cluster IDs are derived and may change after regeneration. UI/history must not treat them as durable person identifiers.
- Incremental clustering can drift from full recomputation if update semantics are underspecified; accepted invariants and deterministic rebuild behavior are required.
- Context signals such as capture time or same-photo co-occurrence can improve confidence but must remain supporting/negative evidence, not opaque identity truth.
- Event-driven regeneration can create excessive background work unless changes are coalesced and bounded.
- Better grouping can make a wrong suggestion look more convincing. UI must expose uncertainty and make exceptions easy before bulk canonical mutation.
