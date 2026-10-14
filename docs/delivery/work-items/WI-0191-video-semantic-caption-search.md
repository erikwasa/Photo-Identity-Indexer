---
id: WI-0191
title: Offer opt-in semantic and caption search over indexed videos
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0190]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, docs]
---

# WI-0191: Offer opt-in semantic and caption search over indexed videos

## Objective

Make video frame evidence discoverable through Visual, Caption and Combined retrieval without changing existing photo Search behavior.

## In scope

- Start as a separate Video Search view or explicit media-type selector with Photos as the unchanged default.
- Reuse currently supported text-to-image query embeddings, caption retrieval and reciprocal-rank fusion where suitable; do not claim photo scores equal calibrated video relevance.
- Aggregate segment/frame evidence into **one result per video revision** with provenance, representative timestamp/interval and explainable top match.
- Filter excluded, inaccessible, unsupported, stale or missing-evidence revisions before ranking; do not trigger new video decoding/hydration during search.
- Keep existing saved photo-result membership and exact Smart Collection filters unchanged; video Smart scope comes only after WI-0153 explicitly defines eligibility.
- Benchmark realistic frame volumes, result limit, latency and query relevance before adopting heavier index technology.

## Out of scope

Audio/subtitle search, mixed-media Creative Collection recipes, live semantic Smart Collection filters and automatic generation on search.

## Acceptance criteria

- [ ] Visual, Caption and Combined modes return videos only when video mode is selected, with one result per source revision.
- [ ] Results retain the grounded matching frame timestamp/description and explicit generated-evidence provenance.
- [ ] Existing photo-only search eligibility, ranking, saving and baseline latency are preserved.
- [ ] Missing/stale/excluded videos never leak; requests are read-only with respect to video processing.
- [ ] Maintainer verifies representative queries with small and large video sets.

## Verification requirements

Query/ranking/access tests and maintained-archive comparison of video search relevance and photo-search regression.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
