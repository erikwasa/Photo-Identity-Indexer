---
id: WI-0115
title: Add cluster-based People-to-identify review workspace
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0114, WI-0060]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests]
---

# WI-0115: Add cluster-based People-to-identify review workspace

## Objective

Turn provisional face clusters into a practical operator workflow that represents many repeated unknown faces as one candidate-person review task while preserving explicit exceptions and canonical human review semantics.

## In scope

- Add a `People to identify` workspace over current provisional clusters.
- Show representative member faces, cluster size, useful date/context range and derived confidence/cohesion information without presenting the cluster as a confirmed person.
- Prioritize larger/high-cohesion clusters while keeping deterministic navigation and a path to smaller clusters/noise.
- Open a cluster into bounded member review with selection, exception removal and source-photo context.
- Allow assignment of selected/core members to an existing person or creation of a new canonical Person through existing audited review semantics.
- Do not require every cluster member to be assigned together; borderline/noise members can remain unreviewed.
- Support explicit `not same person`/split feedback where necessary and persist it as negative discovery evidence suitable for future reclustering.
- Keep touch/mobile interaction viable with representative cards, member drill-down and bulk actions that do not depend on desktop keyboard modifiers.
- Refresh affected cluster state predictably after canonical assignment or negative feedback.

## Out of scope

- Automatically assigning the entire cluster.
- Treating cluster IDs as canonical people.
- Changing known-person suggestion scoring.

## Implementation notes

The implementation reuses the existing audited bulk-review preview/commit path for canonical Person assignment. Provisional cluster membership never writes identity state directly, and unselected members remain unreviewed.

Explicit `not same` feedback is stored as durable canonicalized face-to-face discovery constraints. A feedback action validates that the anchor and selected exceptions still belong to the same current derived cluster, stores the constraints without changing face review state, and queues a replacement run for the same exact model/policy scope. The clustering worker applies those constraints after the selected DBSCAN density result by deterministically partitioning any conflicting derived component; undersized partitions fall back to derived Noise rather than weakening the selected clustering policy.

The review workspace is PostgreSQL-only with the same provider boundary as production provisional clustering. Group cards are ordered primarily by size and then by Core share, expose representative faces and explicit derived/provisional labelling, and member loading is bounded. Source-photo context remains available through the existing face-details route.

Maintainer usability feedback after acceptance identified the GUID-oriented anchor dropdown as unnecessarily disconnected from the face cards. The follow-up polish defaults the anchor to the first displayed member, marks that card directly, and lets the operator choose another anchor with a `Make anchor` action on the desired face. This is a post-acceptance interaction improvement; the maintainer plans to recheck the smoother anchor selection together with a later work item rather than reopening the core WI-0115 acceptance gate.

## Acceptance criteria

- [x] The operator can browse provisional candidate identities as cluster cards rather than only individual face cards.
- [x] Cluster cards show representative faces, member count and clear provisional/derived status.
- [x] Opening a cluster provides bounded member loading and original-photo context needed to detect mistakes.
- [x] The operator can select obvious members, remove exceptions and assign/create a person through canonical audited review actions.
- [x] Borderline members can remain unreviewed instead of being forced into the canonical assignment.
- [x] The operator can record that faces/groups should not be clustered together, and later clustering respects that evidence according to the accepted clustering contract.
- [x] Cluster changes caused by review do not rewrite or delete canonical history.
- [x] The representative workflow is usable on a mobile-width touch interface without thousands of individual queue actions.
- [x] Automated coverage protects cluster membership eligibility, bulk review handoff, negative feedback and post-review refresh behavior.

## Verification requirements

Automated API/Web/persistence coverage plus human Windows and real/mobile-browser verification using clusters with correct members and intentional exceptions.

Maintainer verification completed successfully on 2026-09-14 against the local PostgreSQL-backed application. The operator confirmed the cluster-card/member workflow, selective canonical assignment, preservation of unselected review state, negative discovery feedback/reclustering behavior, canonical-history preservation, and mobile-width interaction. The real catalogue produced predominantly pure clusters containing the same person, so finding an intentional false merge for the anchor scenario required additional searching; this was recorded as useful quality evidence rather than a failure of the workflow.

## Completion

WI-0115 was accepted by the maintainer on 2026-09-14 after PR #334 and its successful CI run. Follow-up PR #336, merged on 2026-09-15, replaced the GUID-oriented anchor selector with direct face-card anchor selection. That polish does not reopen acceptance; its interaction can be visually rechecked together with a later work item.
