---
id: WI-0047
title: Add Unknown as a face review state
milestone: M17
status_source: ../status/work-items.yaml
depends_on: [WI-0015, WI-0016]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0047: Add Unknown as a face review state

## Objective

Allow a detected real face to be marked Unknown when the person cannot currently be identified, without creating a synthetic Person that represents many unrelated identities.

## Why

Unknown people are legitimate face detections and should not be rejected as false detections, but creating a named person for every unidentified face would pollute identity evidence and collections.

## In scope

- Add an append-only, reversible Unknown review action/state with no PersonId.
- Distinguish Unknown from Unreviewed, Assigned and Rejected in filters, counts and face details.
- Exclude Unknown faces from matcher targets, exemplars and person-based suggestion generation by default.
- Exclude Unknown from person collections.
- Allow a later manual assignment to supersede Unknown normally and become canonical identity evidence.
- Preserve the earlier Unknown decision in audit history.
- Provide a future-safe query boundary so Unknown faces can later be intentionally revisited/rematched without changing their meaning.

## Out of scope

- Clustering different unknown faces into anonymous identities.
- Automatically rematching Unknown faces.
- Treating Unknown as a special Person record.

## Acceptance criteria

- [ ] A face can be marked Unknown and later undone or assigned to a person.
- [ ] Unknown is visibly distinct from false-detection rejection.
- [ ] Unknown faces do not become exemplars, ordinary suggestion targets or person-collection evidence.
- [ ] Later manual assignment becomes the active canonical identity while preserving the Unknown history.
- [ ] Review counts and filters include Unknown explicitly.

## Verification requirements

Automated review-state/matcher/collection regression tests plus human UI verification.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
