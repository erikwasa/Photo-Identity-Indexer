---
id: WI-0112
title: Add suggested-person grouped review workspace
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0043, WI-0060]
related_adrs: [ADR-0006]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests]
---

# WI-0112: Add suggested-person grouped review workspace

## Objective

Let the operator process existing ranked identity suggestions by suggested person so likely repetitions of one known identity can be reviewed together rather than appearing as unrelated cards in the broader Faces queue.

## Why

The current review queue can filter/order by confidence, and bulk suggestion acceptance already exists, but discovering and processing all faces suggested for one person still requires more queue navigation than necessary. Grouping by suggested identity provides immediate review-effort reduction without changing recognition semantics or introducing clustering.

## In scope

- Add a view/grouping keyed by exact-model rank-1 suggested person.
- Show group size, confidence distribution and representative faces without implying that all group members are correct.
- Order groups using useful review signals such as strongest confidence, count and favorite-person status while keeping deterministic behavior.
- Open a group into the existing face-review selection model so the operator can accept obvious matches, remove exceptions and reject incorrect face-person suggestions.
- Preserve rank-1/rank-2 score and margin visibility for individual members.
- Keep rejected suggestion pairs excluded according to existing semantics.
- Preserve pagination/bounded queries for large groups.
- Provide a touch/mobile interaction path for reviewing and bulk accepting a subset.

## Out of scope

- Changing suggestion scoring or thresholds.
- Treating a grouped suggestion as a face cluster.
- Automatically accepting a whole group because its members share one rank-1 person.
- Discovering people not already represented by canonical exemplars.

## Acceptance criteria

- [ ] The operator can browse pending rank-1 suggestions grouped by suggested person for one exact model revision.
- [ ] Each group exposes count and useful confidence summary information plus representative faces.
- [ ] Opening a group provides bounded member loading and existing score/margin evidence per face.
- [ ] The operator can select/accept a subset and remove exceptions without accepting the whole group.
- [ ] Incorrect suggestions can be rejected and remain durable negative face-person evidence.
- [ ] Group counts/content update predictably after accept/reject/review actions.
- [ ] The workflow remains usable on touch/mobile layouts.
- [ ] Automated coverage protects exact-model scoping, pending/rejected eligibility, paging and grouped bulk-review semantics.

## Verification requirements

Automated API/persistence integration coverage plus human Windows and mobile-browser verification against a person with multiple pending suggestions and at least one intentional exception.
