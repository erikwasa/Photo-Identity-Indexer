---
id: M36
title: Semantic collection composition
status_source: ../status/milestones.yaml
depends_on: [M26]
---

# M36: Semantic collection composition

## Outcome

Photo Identity can combine exact Smart Collection eligibility with ranked Visual/CLIP and generated-caption retrieval, then reuse that same scoped retrieval boundary as an optional anchor source for Creative Collections.

The milestone preserves the existing semantic boundaries: Smart Collection criteria remain deterministic catalogue filters, semantic/caption evidence remains derived and ranked, explicit saved search-result collections remain immutable photo-list snapshots, and Creative Collections remain regenerable presentation recipes whose final slideshow sessions use immutable revision snapshots.

## Delivery principles

- Keep Smart Collection semantics exact and model-independent; do not add fuzzy search fields to `SmartCollectionFilter`.
- Apply structured eligibility before semantic/caption ranking so a ranked result can never escape the selected Smart scope.
- Reuse one server-side scoped-search boundary across ordinary Search and Creative Collection anchor generation.
- Preserve search provenance for Visual, Caption and Combined evidence instead of collapsing scores into canonical metadata.
- Keep saved search-result photo lists immutable, as established by WI-0162.
- Treat search-anchored Creative recipes as derived presentation definitions: later model/caption evidence may change future materializations, but already-created slideshow snapshots do not change.
- Bound search-anchor admission by rank/result count rather than relying on a universal cosine threshold.
- Do not revive the rejected WI-0127 embedding-diversity path merely because semantic retrieval becomes a Creative anchor source.

## Work items

- [WI-0186](../work-items/WI-0186-scoped-semantic-caption-search.md) - restrict Visual/Caption/Combined search to exact Smart Collection eligibility while preserving ranking and provenance.
- [WI-0187](../work-items/WI-0187-search-creative-anchors.md) - allow Creative Collections to use bounded semantic/caption search results, optionally Smart-scoped, as direct anchors for normal moment/context expansion.

## Delivery sequence

1. WI-0186 establishes the reusable scoped-search contract and verifies semantic, caption and combined retrieval inside exact Smart eligibility.
2. WI-0187 reuses that contract for Creative Collection anchor materialization instead of introducing a parallel retrieval implementation.

## Exit criteria

- [ ] A user can run Visual, Caption or Combined search inside an exact Smart Collection scope with deterministic eligibility and preserved result provenance.
- [ ] Scoped search is server-side, bounded and archive-scale practical without sending a complete candidate revision list through the browser.
- [ ] Existing unscoped Search behavior and immutable saved result collections remain compatible.
- [ ] A Creative Collection can use a persisted bounded search-anchor definition while existing Smart-anchored recipes remain unchanged.
- [ ] Search-anchored Creative preview/materialization clearly distinguishes direct search anchors from moment/context additions and remains bounded on representative archive data.
- [ ] Search/model/caption evidence changes can affect future Creative materializations without mutating already-created slideshow snapshots.
- [ ] Maintainer verifies representative structured + visual/caption queries and at least one search-anchored Creative Collection on the maintained archive.
