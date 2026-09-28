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

Current materialization loads the catalogue-wide photo set, clusters moments, expands candidates, resolves review proxies and recomputes perceptual hashes for candidate visual-redundancy evidence with bounded concurrency before final selection. The target count therefore does not bound the expensive pre-selection work.

## Scope

- Establish archive-scale timing diagnostics for Creative materialization, including candidate count and major phase durations without logging private photo paths or metadata.
- Avoid recomputing stable visual-redundancy evidence from durable review proxies on every preview/materialization when that evidence can be safely cached or persisted as derived/versioned state.
- Bound catalogue and candidate work where possible without changing the accepted Creative Collection semantics for anchors, moments, Broad context, diversity, presentation preferences or novelty.
- Ensure a target count such as 150 does not require unbounded repeated image decoding/hash computation merely to preview the selection.
- Keep missing/unreadable review proxies non-fatal and do not hydrate originals solely for Creative materialization.
- Give the UI an explicit busy/progress/failure outcome rather than appearing frozen until a generic client timeout if a bounded server-side operation still cannot complete.
- Preserve deterministic selection for the same inputs/policy versions except where a documented performance-oriented contract change is explicitly accepted.

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
2. Generate the same preview twice and confirm stable derived visual evidence is reused on the second run rather than recomputed from every proxy.
3. Verify the resulting selection remains semantically equivalent under the accepted policy versions.
4. Change or invalidate representative derived evidence and confirm only the necessary work is regenerated.
5. Verify missing/unreadable proxies remain non-fatal and no original hydration is triggered.
6. Repeat from the phone/PWA and confirm the UI completes or reports a bounded actionable failure rather than freezing until the HTTP request is cancelled.
