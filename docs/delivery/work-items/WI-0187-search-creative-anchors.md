---
id: WI-0187
title: Use semantic and caption search results as Creative Collection anchors
milestone: M36
status_source: PhotoIdentity.Docs
depends_on: [WI-0186]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0187: Use semantic and caption search results as Creative Collection anchors

## Context

Issue #513 tracks the second composition step after WI-0186.

Creative Collections currently persist a Smart Collection anchor and materialize its exact matches as direct anchors before moment/context expansion and deterministic presentation selection. The lower-level candidate generator already fundamentally consumes revision IDs, while WI-0162 provides ranked Visual/Caption/Combined retrieval. This makes semantic search a natural alternative anchor source without changing Smart Collection semantics.

The goal is not to turn embeddings into tags or revive nearest-neighbor diversity. The goal is to let a natural-language concept identify the direct anchor pool, then reuse the established Creative Collection machinery to add nearby moment context and build a bounded slideshow.

## Scope

- Generalize the Creative Collection recipe anchor from only a Smart Collection reference to a versioned anchor-source definition.
- Preserve the existing Smart Collection anchor source as the default/backward-compatible shape.
- Add a search anchor source that persists at minimum:
  - natural-language query;
  - search mode: Visual, Captions or Combined;
  - optional Smart Collection scope using WI-0186;
  - bounded maximum number of ranked search anchors;
  - version/policy information required to interpret the anchor definition.
- Reuse the WI-0186 scoped-search execution path to materialize direct search anchors.
- Admit anchors by deterministic top-N/rank policy rather than one universal cosine threshold.
- Carry enough provenance into preview/materialization to explain that a direct anchor came from semantic visual evidence, generated-caption evidence or both.
- Feed the admitted direct revision IDs into the existing moment/context expansion path. Context-only photos must not become recursive semantic anchors.
- Preserve the existing Creative target count, context strength, novelty/presentation preferences, visual redundancy behavior and final slideshow snapshot boundary unless a narrowly required compatibility change is documented.
- Keep materialization bounded: search anchor count is finite before moment expansion, and existing Creative request/materialization limits remain enforceable.
- Keep existing Smart-anchored Creative recipes readable and behaviorally unchanged after schema evolution.

## Relevance and selection semantics

Search ranking determines which revisions enter the direct anchor pool. Creative selection then remains responsible for producing a diverse presentation from those anchors and their context.

If implementation evidence shows that a large admitted anchor pool causes lower-ranked semantic hits to displace clearly better matches, a bounded search-rank preference may be added to Creative selection. Such a preference must remain explicit/versioned and must not present raw cosine score as canonical photo quality.

This item must not re-enable WI-0127 embedding-diversity selection. Semantic evidence here establishes topic relevance for anchors; it is not a replacement for the existing presentation-diversity policy.

## Persistence semantics

A search-anchored Creative Collection is a regenerable presentation recipe, not an immutable search-result collection.

Therefore:

- future caption additions, embedding regeneration or an explicitly adopted search-model/policy change may alter future preview/materialization results;
- the UI/documentation must not imply that search-anchored Creative membership is permanently frozen;
- once a slideshow session/snapshot has been created, its revision membership remains immutable under the existing slideshow boundary;
- explicit photo-list collections saved from ordinary Search remain the correct choice when the user wants frozen search membership.

## UX expectations

The Creative Collection authoring experience should allow choosing an anchor source without turning the page into a database-query builder. A practical first surface can offer:

- Smart Collection anchor; or
- Search anchor with query, mode, optional Smart scope and anchor limit.

Preview should make the distinction between direct search matches and added contextual photos understandable, including the search query/source provenance where practical.

## Out of scope

- A general-purpose persisted Hybrid Collection type outside Creative Collections.
- Canonical automatic tagging from semantic search.
- Similar-photo browsing or nearest-neighbor Creative diversity.
- Automatic query generation from captions.
- Changing the underlying CLIP/caption generation models.
- Replacing immutable slideshow snapshots with live/dynamic playback membership.

## Acceptance criteria

- [ ] Existing persisted Smart-anchored Creative recipes load, preview, update and materialize with unchanged semantics.
- [ ] A new Creative recipe can persist and reopen a search anchor containing query, mode, optional Smart scope and a bounded anchor limit.
- [ ] Search anchors are materialized through the WI-0186 retrieval path rather than a duplicate semantic/caption implementation.
- [ ] Direct anchor admission is deterministic and bounded by rank/result count; the implementation does not depend on one universal cosine threshold.
- [ ] Visual, Caption and Combined search provenance is retained sufficiently for preview/debugging and is not converted into canonical tags/metadata.
- [ ] Existing moment/context expansion works from search anchor revision IDs, and context-only photos never become recursive search anchors.
- [ ] Creative materialization remains bounded for large searches and reports actionable failures rather than allowing unbounded anchor expansion.
- [ ] Already-created slideshow snapshots remain immutable even if later captions/embeddings/search evidence would change a future Creative materialization.
- [ ] Automated coverage protects legacy recipe compatibility, persistence round-trip, scoped/unscoped search anchors, deterministic top-N admission, context expansion and snapshot immutability.
- [ ] Maintainer verifies at least two representative search-anchored Creative Collections on the private archive, including one with a Smart scope, and confirms the resulting slideshow is more useful than the raw ranked result list alone.

## Verification requirements

Use representative concepts with enough matching photos to exercise both anchor ranking and context expansion, such as a recurring activity, seasonal scene or event object. For each verification case record the query/mode/scope, admitted anchor count, context additions, requested/selected count and materialization time. Confirm several previewed direct anchors are genuinely relevant, contextual additions belong to sensible moments, and rerunning after ordinary catalogue/search evidence changes never mutates an already-created slideshow snapshot.
