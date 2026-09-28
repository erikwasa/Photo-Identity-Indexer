---
id: WI-0169
title: Bound Creative Collection materialization for large Broad selections
milestone: M32
status_source: ../status/work-items.yaml
depends_on: [WI-0120, WI-0121, WI-0123]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0169: Bound Creative Collection materialization for large Broad selections

## Objective

Make Creative Collection preview and slideshow materialization remain responsive for realistic large Smart Collection anchors and Broad context expansion, instead of recomputing expensive derived image work until the browser request times out.

## Maintainer evidence

A Smart Collection returning roughly 1,000 photos was used as the anchor for a Creative Collection with target count 150 and **Broad** context. The recipe save completed successfully, but the Creative section appeared frozen. The attached runtime log showed the subsequent request

`GET /api/smart-collections/{id}/creative-preview?targetCount=150&momentGapMinutes=30&contextStrength=broad&novelty=false`

running for approximately 100 seconds before ending with HTTP 499. A repeated attempt exhibited the same approximately 100-second cancellation.

The previous materialization path loaded the catalogue-wide photo set, clustered moments, expanded candidates, resolved review proxies and recomputed perceptual hashes for every generated candidate with four workers before final selection. The target count therefore did not bound the expensive image work.

## Scope

- Establish archive-scale timing diagnostics for Creative materialization, including candidate count and major phase durations without logging private photo paths or metadata.
- Avoid recomputing stable visual-redundancy evidence from durable review proxies on every preview/materialization when that evidence can be safely cached or persisted as derived/versioned state.
- Bound catalogue and candidate work where possible without changing the accepted Creative Collection semantics for anchors, moments, Broad context, diversity, presentation preferences or novelty.
- Ensure a target count such as 150 does not require unbounded repeated image decoding/hash computation merely to preview the selection.
- Keep missing/unreadable review proxies non-fatal and do not hydrate originals solely for Creative materialization.
- Give the UI an explicit busy/progress/failure outcome rather than appearing frozen until a generic client timeout if a bounded server-side operation still cannot complete.
- Preserve deterministic selection for the same inputs/policy versions except where a documented performance-oriented contract change is explicitly accepted.

## Implementation

### Exact visual-evidence pruning

The accepted visual-redundancy policy can only place two photos in the same visual group when they are in the same inferred moment and the group's capture span is at most 20 seconds. Creative materialization now uses that invariant before touching image bytes: a candidate is eligible for perceptual hashing only when another generated candidate in the same inferred moment is within the accepted 20-second span. A photo with no such temporal neighbour can only form a singleton visual group, which the existing grouper discards, so omitting its hash does not change suppressible/grouped output.

This is a semantic pruning rule, not a target-count truncation. All direct anchors and context candidates still participate in the accepted Creative selection policy. The optimization only avoids deriving evidence that cannot affect the visual-redundancy result.

### Versioned proxy-hash reuse

Successful dHash evidence is cached process-locally by:

- immutable asset revision id;
- durable review-proxy SHA-256 content hash;
- `PhotoVisualRedundancyPolicy.AlgorithmVersion`.

A regenerated proxy therefore receives a different key, and a future perceptual-hash algorithm version cannot reuse old evidence accidentally. The Creative materialization service is a singleton, so unchanged proxy evidence is reusable across repeated preview/slideshow materializations during the running Photo Identity process. Missing or unreadable proxy evidence is not cached as a permanent failure.

### Set-oriented proxy/privacy resolution

The review-proxy resolver now resolves a candidate batch through the repository's existing `GetManyAsync` path. Source-copy exclusion checks also have a set-oriented PostgreSQL implementation so the privacy boundary remains in force without one exclusion query per candidate. Resolved files retain the existing derivative-root, encoded-length and reparse-point checks and never fall back to source originals.

### Bounded hashing and diagnostics

Cache misses are hashed with a bounded worker gate of 4–12 workers based on available processors rather than a fixed four-worker limit. Materialization emits aggregate, path-free diagnostics for anchor count, catalogue size, candidate count, target count, visual-eligible count, resolved proxies, cache hits, hashes computed, missing/unreadable proxies, major phase durations and total elapsed time.

### Bounded request failure and UI state

Creative preview and creative slideshow-snapshot materialization use a 60-second linked server deadline. A server deadline returns HTTP 503 with a Creative-specific error instead of allowing the request to drift into the previously observed approximately 100-second client cancellation. Client cancellation still propagates normally.

The existing Smart Collections `Busy` state now becomes visibly explicit in the Creative section: the status/button changes to **Working…** and an aria-live message explains that Creative work is active. Errors continue through the existing visible alert surface.

## Automated coverage

- Cache tests verify exact reuse for the same revision/proxy-content-hash/algorithm tuple and invalidation when any of those inputs changes.
- Semantic-equivalence coverage compares visual groups from the full candidate fingerprint set with groups after exact 20-second temporal pruning.
- A non-private 1,000-candidate scaling guard verifies that sparse candidates outside the accepted 20-second visual span require zero perceptual-hash work.
- Existing Creative selection/candidate tests continue to protect anchor/context generation, deterministic diversity, novelty and presentation-preference behavior.
- Existing proxy resolution and source-copy exclusion contracts remain in force; the optimized path never opens or hydrates source originals.

## Acceptance criteria

- [ ] A representative Smart Collection of approximately 1,000 anchor photos can generate a 150-photo Broad Creative preview without hitting the current approximately 100-second client cancellation boundary on the maintained machine.
- [ ] Repeated preview/materialization does not recompute unchanged perceptual hashes for the same durable proxy set unnecessarily.
- [ ] Expensive derived evidence is versioned/invalidation-safe when proxies or the accepted visual-redundancy policy change.
- [ ] The optimization preserves accepted anchor/context/selection semantics and does not hydrate original photos.
- [ ] The UI no longer appears indefinitely frozen; bounded failure is surfaced clearly when materialization cannot complete.
- [ ] Privacy-safe diagnostics make the dominant materialization phases measurable at archive scale.
- [ ] Automated tests cover cache/persistence reuse and invalidation plus unchanged selection semantics; an archive-scale timing check records the representative large/Broad case.

## Verification plan

1. Reproduce the maintainer case with a Smart Collection of roughly 1,000 matches, target 150, Broad context and novelty off; record candidate counts and phase timings.
2. Generate the same preview twice and confirm the first run computes only temporally eligible visual evidence and the second run records cache hits instead of recomputing unchanged proxy hashes.
3. Verify the resulting selection remains semantically equivalent under the accepted policy versions.
4. Change/regenerate a representative durable review proxy and confirm its changed content hash forces only that evidence to be recomputed.
5. Verify missing/unreadable proxies remain non-fatal and no original hydration is triggered.
6. Repeat from the phone/PWA and confirm the Creative section visibly enters Working state and then completes or reports a bounded actionable failure rather than freezing until the HTTP request is cancelled.

## Verification status

Implementation and automated scaling/reuse guards are present on the WI-0169 branch. The acceptance checkboxes intentionally remain open until CI passes and the maintained archive reproduces the reported approximately 1,000-anchor / target-150 / Broad case with before/after timing evidence.
