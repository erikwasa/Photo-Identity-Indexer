---
id: WI-0112
title: Add suggested-person grouped review workspace
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0043, WI-0060]
related_adrs: [ADR-0006]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests]
---

# WI-0112: Add suggested-person grouped review workspace

## Objective

Let the operator process existing ranked identity suggestions by suggested person so likely repetitions of one known identity can be reviewed together rather than appearing as unrelated cards in the broader Faces queue.

## Why

The current review queue can filter/order by confidence, and bulk suggestion acceptance already exists, but discovering and processing all faces suggested for one person still requires more queue navigation than necessary. Grouping by suggested identity provides immediate review-effort reduction without changing recognition semantics or introducing clustering.

## Implementation status

PR #324 implements a bounded `Suggested groups` discovery surface over the existing suggestion/review workflow. The grouping query is exact-model scoped and includes only currently unreviewed faces whose rank-1 suggestion is still `pending`; an assigned/Unknown/false-detection face or a rejected face-person suggestion therefore disappears from the group without creating another identity state.

Each group exposes pending count, High/Medium/Low distribution from the same persisted exact-model confidence policy, strongest score/margin, favorite-person status and up to four deterministic representative faces. Ordering is deterministic: favorite people first, then strongest available confidence category, pending count, strongest score, display name and person ID.

Opening a group deep-links into the existing Faces workspace with the exact model revision and suggested-person filter. That intentionally reuses the proven paged member loading, per-face score/margin evidence, touch-friendly checkbox selection, `Select loaded`, exception removal and audited bulk suggestion acceptance. Incorrect face-person suggestions continue to use the existing suggestion-rejection action from Face Details; they are not conflated with the face-level `False detection` decision.

The summary page never writes canonical review state and never accepts a whole group automatically. There is no schema, recognition-score, threshold or clustering change. PostgreSQL is the production query implementation; SQLite carries the same read-only contract for compatibility/integration coverage.

Human acceptance is intentionally deferred to the combined maintainer session for WI-0111, WI-0112 and WI-0113. WI-0112 remains `in_review` until the desktop and touch/mobile subset-review path is verified against real pending suggestions.

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

Automated API/persistence integration coverage plus human verification against a person with multiple pending suggestions and at least one intentional exception. On desktop, verify group summaries, bounded member loading, selective acceptance, durable rejection and count refresh. Repeat the key subset-selection/acceptance flow from a mobile/touch browser layout. Perform that pass in the combined WI-0111/WI-0112/WI-0113 maintainer session before WI-0112 is completed.
