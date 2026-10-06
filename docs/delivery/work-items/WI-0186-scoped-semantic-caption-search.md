---
id: WI-0186
title: Scope visual and caption search with Smart Collection criteria
milestone: M36
status_source: PhotoIdentity.Docs
depends_on: [WI-0162]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0186: Scope visual and caption search with Smart Collection criteria

## Context

Issue #512 tracks the first composition step between the existing exact Smart Collection query model and WI-0162 ranked photo search.

Smart Collections currently express deterministic eligibility using people, tags, Places/GPS, dates, age, relationships and other structured criteria. Photo Search separately ranks the archive using Visual/CLIP similarity, generated-caption text, or reciprocal-rank fusion of both. These systems are complementary but should not be conflated: structured criteria answer whether a revision is eligible, while semantic/caption evidence answers how relevant an eligible revision is to a natural-language query.

The required product behavior is therefore a scoped search, not a fuzzy Smart Collection.

## Scope

- Add an optional Smart Collection scope to the existing Photo Search workflow.
- Reuse the existing Smart Collection filter/query semantics as the source of eligible revision IDs. The initial Web experience may select a saved Smart Collection rather than duplicating the complete Smart Collection editor inside Search.
- Establish a reusable server-side search-scope contract so semantic, caption and combined modes operate over the same eligible revision set.
- Apply scope before final ranking/limit:
  - semantic retrieval must ignore embeddings outside the eligible set;
  - caption retrieval must restrict its PostgreSQL search to eligible revisions;
  - combined reciprocal-rank fusion must only combine eligible hits.
- Keep the existing result provenance, semantic score, caption score, caption language/content and source labels intact.
- Preserve existing unscoped Search behavior when no Smart scope is selected.
- Preserve the current result-limit contract, including the 1,000-result product maximum.
- Keep the existing WI-0162 save boundary: selecting and saving search results still creates/updates an explicit ordered photo-list collection whose membership does not later change with model/caption evidence.
- Keep the full eligible revision set on the server. Do not serialize thousands of revision IDs through the browser merely to scope a request.
- Avoid N+1 catalogue/repository lookups when resolving a Smart Collection scope.

## Architecture constraints

Do not add a semantic query string or ranking fields to `SmartCollectionFilter`. Smart Collections remain exact, model-independent definitions.

A small reusable abstraction such as a server-side candidate scope/revision predicate is preferred over teaching each retrieval source about Smart Collection internals. The concrete implementation may materialize Smart eligibility as a bounded in-memory set for the current archive scale or push the restriction into PostgreSQL where appropriate, but semantic and caption modes must observe identical eligibility semantics.

No new vector database or ANN infrastructure is justified by this item unless measurement demonstrates the existing exact-search approach has become inadequate.

## UX expectations

The user should be able to express flows such as:

- saved Smart Collection: `Fideli 2019-2024`;
- natural-language query: `playing by water`;
- mode: Visual, Captions or Combined;
- results: only revisions that exactly satisfy the Smart scope, ranked by the selected search evidence.

The active scope must be visible and easy to clear. Empty Smart scopes and zero-result ranked searches should be distinguishable from service/model failures.

## Out of scope

- Persisting a new dynamic Hybrid Collection type.
- Adding natural-language search fields to saved Smart Collection definitions.
- Changing Smart Collection matching from exact predicates to ranked/fuzzy semantics.
- Changing CLIP model choice, caption generation policy or supported Visual query language.
- Replacing explicit saved search-result collections with dynamic membership.
- Creative Collection integration; that is WI-0187.

## Acceptance criteria

- [ ] Photo Search can run unscoped or inside an optional Smart Collection scope in Visual, Captions and Combined modes.
- [ ] Every scoped result satisfies the selected Smart Collection's current exact membership; no semantic/caption result can escape the scope.
- [ ] Semantic retrieval applies the eligibility set before final ranking/limit and does not require captions to exist.
- [ ] Caption retrieval applies the same eligibility semantics in PostgreSQL without N+1 per-revision queries.
- [ ] Combined reciprocal-rank fusion only combines eligible semantic/caption hits and retains source-specific provenance/scores.
- [ ] Clearing the scope restores existing unscoped behavior without changing ranking semantics.
- [ ] Existing search-result selection, cumulative selection and save/append-to-manual-collection workflows continue to work and still freeze explicit revision membership.
- [ ] The browser does not receive or resend the complete Smart candidate revision set solely to execute scoped search.
- [ ] Automated coverage protects saved-scope resolution, semantic-only, caption-only, combined, empty-scope, missing-caption and unscoped compatibility behavior.
- [ ] Representative maintained-archive verification confirms useful mixed queries and records scoped-search latency for at least one small and one large Smart Collection.

## Verification requirements

On the maintained archive, verify at least three combinations where the structured constraint is independently inspectable, for example person + visual activity, date/place + scene, and a scope containing photos without generated captions. Confirm every result belongs to the selected Smart Collection and compare unscoped versus scoped latency/result behavior. Verify saving a scoped result set still produces an immutable explicit photo-list collection after restart.
