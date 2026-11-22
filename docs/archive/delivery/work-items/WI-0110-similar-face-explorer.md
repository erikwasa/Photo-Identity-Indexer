---
id: WI-0110
title: Add similar-face explorer for immediate identity discovery
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0060, WI-0103]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Tests, PhotoIdentity.Integration.Tests]
---

# WI-0110: Add similar-face explorer for immediate identity discovery

## Objective

Let the operator start from one eligible face and immediately surface the most similar other faces so repeated appearances of an unknown person can be reviewed and bulk-assigned together instead of waiting to encounter them later in the normal queue.

## Why

The current matcher compares unreviewed targets with known-person exemplars. It does not provide a face-to-face discovery workflow for an identity that has not yet been named. When an operator recognises one previously unknown face, other appearances can remain scattered across a large queue even though their embeddings are already available.

## In scope

- Add a provider-neutral nearest/similar-face query over one exact embedding-model revision.
- Start from an eligible face occurrence and return a bounded, deterministic similarity-ranked result set excluding the source face.
- Default discovery to unreviewed valid faces; allow intentional inclusion of canonical Unknown faces as an explicit filter without changing their review state.
- Exclude rejected/false-detection faces and faces without the selected exact-model embedding.
- Show similarity evidence and enough source context to review results without implying that similarity is a canonical identity decision.
- Reuse existing selection/bulk assignment behavior so obvious matches can be assigned to an existing/new person while exceptions remain unselected.
- Support touch/mobile selection without depending on desktop Shift-click behavior.
- Keep query cost bounded for archive scale; exact scan is acceptable initially if measured latency is practical, otherwise introduce a PostgreSQL vector-search implementation behind the same contract.
- Add integration coverage for eligibility, exact-model isolation, ordering, Unknown opt-in and bulk-review handoff.

## Out of scope

- Creating a persistent face cluster.
- Automatically assigning all returned neighbours.
- Changing identity-suggestion High/Medium/Low thresholds.
- Treating nearest-neighbour rank as proof that two faces are the same person.

## Acceptance criteria

- [x] An operator can invoke `Find similar faces` from an eligible face in the review experience.
- [x] Results are ordered deterministically by similarity for one exact embedding revision and exclude the source face.
- [x] Rejected faces and faces without a matching exact-model embedding are not returned.
- [x] Canonical Unknown faces are excluded by default and may be included only through an explicit rediscovery option.
- [x] Including Unknown faces does not alter their canonical Unknown state.
- [x] The operator can select a subset of returned faces, remove exceptions and assign the remaining selection together with the unreviewed source face through existing audited review semantics.
- [x] The view works on mobile/touch without requiring range-selection keyboard modifiers.
- [x] Archive-scale query latency is measured and bounded; any vector index is an implementation detail rather than part of the API contract.
- [x] Automated coverage protects exact-model scope, deterministic ranking and review-state eligibility.
- [x] While a similar-face selection is active, the bulk action controls remain fixed in the viewport like the ordinary Review workspace rather than appearing only after scrolling to the end of the result list.
- [x] Automatic source inclusion applies only to `Assign` while the source is still unreviewed; `Unknown` and `False detection` remain explicit-selection-only and no already-reviewed source is silently rewritten.

## Maintainer verification — 2026-09-13

Real-catalogue Windows/mobile verification after PR #319 established that the core discovery workflow behaves correctly and is fast enough to retain the bounded exact PostgreSQL scan for this slice:

- 100 ranked results returned from 9,847 eligible faces in 151 ms.
- The first 20 returned faces were all correct matches for the chosen source face.
- Exact-model discovery, source exclusion, Unknown opt-in, subset selection, audited bulk action behavior and the requested desktop/mobile checks behaved as expected.

The first pass found two corrective items: the similar-face bulk bar was not fixed in the viewport, and assigning selected matches did not include the unreviewed source face. PR #320 corrected both behaviors, kept Unknown/reject explicit-selection-only, and retained stale-state revalidation. The maintainer then confirmed both previously reported issues were addressed.

The measured 151 ms exact scan over 9,847 eligible faces does not justify adding a vector index in WI-0110. Revisit ANN/pgvector only if later archive-scale measurements materially change this result.

## Verification requirements

Completed through automated API/persistence integration coverage plus maintainer Windows/mobile-browser verification using a representative face with multiple known repetitions, followed by focused re-verification of the PR #320 corrective behaviors.
