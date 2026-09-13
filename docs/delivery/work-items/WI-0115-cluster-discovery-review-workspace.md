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

## Acceptance criteria

- [ ] The operator can browse provisional candidate identities as cluster cards rather than only individual face cards.
- [ ] Cluster cards show representative faces, member count and clear provisional/derived status.
- [ ] Opening a cluster provides bounded member loading and original-photo context needed to detect mistakes.
- [ ] The operator can select obvious members, remove exceptions and assign/create a person through canonical audited review actions.
- [ ] Borderline members can remain unreviewed instead of being forced into the canonical assignment.
- [ ] The operator can record that faces/groups should not be clustered together, and later clustering respects that evidence according to the accepted clustering contract.
- [ ] Cluster changes caused by review do not rewrite or delete canonical history.
- [ ] The representative workflow is usable on a mobile-width touch interface without thousands of individual queue actions.
- [ ] Automated coverage protects cluster membership eligibility, bulk review handoff, negative feedback and post-review refresh behavior.

## Verification requirements

Automated API/Web/persistence coverage plus human Windows and real/mobile-browser verification using clusters with correct members and intentional exceptions.
