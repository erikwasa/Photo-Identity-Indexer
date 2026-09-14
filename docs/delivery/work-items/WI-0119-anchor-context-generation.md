---
id: WI-0119
title: Generate Creative Collection anchors and moment context
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0118, WI-0050]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Core.Tests, PhotoIdentity.Persistence.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0119: Generate Creative Collection anchors and moment context

## Objective

Build the first Creative Collection candidate generator: exact Smart Collection matches become anchors, and the generator may add provenance-preserving context photos from the same inferred moments without changing Smart Collection filter semantics.

## Why

A slideshow about a person or group often needs surrounding photographs that do not contain those people: the cake between portraits, another family member in the same session, the room, a pet or another scene from the same memory. Hard person filtering currently removes those photos completely.

The generator should solve that presentation problem explicitly instead of weakening the user's query or changing what a Smart Collection means.

## In scope

- Accept a saved Smart Collection or equivalent exact filter result as the anchor source.
- Preserve every anchor's direct-match provenance and never report a context-only photo as if it satisfied the original Smart Collection filter.
- Expand anchors through WI-0118 moment membership using a bounded, versioned context policy.
- Support context photos with no selected person and, when useful, no identified people at all.
- Deduplicate candidates reached from multiple anchors/moments while retaining all relevant provenance.
- Define simple inspectable context strengths/policies such as focused/balanced/broad only if evaluation shows they are useful; avoid exposing raw heuristic knobs unnecessarily.
- Expose direct-anchor count, added-context count and total candidate count before final target-size selection.
- Preserve deterministic chronological candidate ordering with immutable revision identity.
- Keep generation read-only and compatible with existing Smart Collection and slideshow security/source-exclusion boundaries.
- Provide a preview that lets the maintainer inspect why each context photo was included.

## Out of scope

- Changing `SmartCollectionFilter`, `any`/`all` matching or saved Smart Collection membership semantics.
- Selecting the final requested number of photos; WI-0120 owns curation/target count.
- Semantic image understanding, aesthetic quality scoring or visual similarity.
- Automatically naming inferred moments or generating narrative captions.
- Persisting context membership as canonical photo metadata.

## Acceptance criteria

- [ ] Existing Smart Collection query results are unchanged by the feature.
- [ ] Every Creative Collection candidate is classified at minimum as a direct anchor or contextual addition, and context additions retain the moment/anchor reason that admitted them.
- [ ] A people-based anchor collection can include same-moment photos where the selected person is not depicted.
- [ ] Context expansion is bounded and cannot recursively spread from context-only photos into unrelated later moments.
- [ ] Candidates reached through multiple anchors are emitted once while preserving useful provenance.
- [ ] The preview/API reports direct-anchor, context and total counts so a user can understand the expansion.
- [ ] Zero-anchor input produces an explicit empty/no-anchor result rather than silently broadening the query.
- [ ] Same input catalogue, Smart Collection, moment policy and context policy produce a deterministic candidate set and order.
- [ ] Automated tests cover person anchors, no-person context, overlapping moments/anchors, zero anchors, context bounds and exclusion/security invariants.
- [ ] Human verification demonstrates at least one private family-photo sequence where context expansion produces a more coherent slideshow candidate set than strict person filtering alone.

## Verification requirements

Automated Core/persistence/API tests are required for exact-anchor semantics, bounded expansion and provenance. Human maintainer verification is required on a private representative collection to confirm that added context is understandable and relevant enough to proceed to WI-0120.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
