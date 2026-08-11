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

- [x] A face can be marked Unknown and later undone or assigned to a person.
- [x] Unknown is visibly distinct from false-detection rejection.
- [x] Unknown faces do not become exemplars, ordinary suggestion targets or person-collection evidence.
- [x] Later manual assignment becomes the active canonical identity while preserving the Unknown history.
- [x] Review counts and filters include Unknown explicitly.

## Verification requirements

Automated review-state/matcher/collection regression tests plus human UI verification. Human Windows laptop and Pixel verification completed on 2026-08-11 as part of the milestone-wide M17 review.

## Completion notes

- Files changed: schema-v12 review-action migration; canonical review/filter/suggestion/bulk repositories and API; matcher, auto-assignment, collection, person-audit and evaluation evidence boundaries; Home/Face Details/face-card UI; architecture/glossary documentation; migration compatibility fixtures; and dedicated Unknown regression coverage.
- Trade-offs: Unknown remains a human-controlled canonical face state. Normal regeneration excludes it; the explicit `UnreviewedAndUnknown` matcher scope can regenerate advisory suggestions for a deliberate future revisit without changing the stored Unknown state. Automatic assignment still refuses Unknown, and suggestion accept/reject requires the active Unknown decision to be undone first. Direct manual assignment is the intentional supersession path and preserves append-only history.
- Integration cleanup: schema version 12 centralizes WI-0044 `person_favorites` in the normal migration lifecycle and moves favorite OR-consolidation into the canonical person-merge transaction, closing the branch-isolation debt recorded by WI-0044.
- Verification: GitHub Actions run `31438925940` passed restore, Release build, the full automated test suite, living-document validation, generated-document checks, review-application smoke verification, Windows PowerShell mixed-media verification and report assertion. Dedicated tests cover Unknown persistence/filtering, distinction from false detection, assignment/undo supersession history, default matcher exclusion, explicit advisory rematch, auto-assignment refusal, collection exclusion and hidden older assignments not becoming exemplars. The maintainer then accepted the integrated Unknown workflow during milestone-wide Windows laptop and Pixel verification on 2026-08-11.
- Deferred work: no completion blocker remains. Automatic Unknown rematching/clustering stays intentionally out of scope.
