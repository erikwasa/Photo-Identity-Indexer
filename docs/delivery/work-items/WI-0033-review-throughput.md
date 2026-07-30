---
id: WI-0033
title: Accelerate the human review workflow
milestone: M06
status_source: ../status/work-items.yaml
depends_on: [WI-0027, WI-0029]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests, PhotoIdentity.ReviewVerification]
---

# WI-0033: Accelerate the human review workflow

## Objective

Reduce the interaction cost of sustained human review on Windows and Pixel without weakening explicit confirmation, append-only audit history, exact model provenance or local privacy boundaries.

## Pilot finding

The 500-image acceptance pilot completed successfully, but review was too slow on both device types. The current gallery does not expose suggestion context, details pages do not preserve queue position, suggestion acceptance reloads the same face, and people cannot be audited through their assigned-face set. The pilot did not capture elapsed review time, so this work item must establish a repeatable throughput measurement while implementing the improvements.

## Acceptance criteria

- [x] Creating a person can be submitted with Enter on Windows and the mobile keyboard action on Pixel without duplicate creation.
- [x] Face details preserve the originating review state, processing-run scope, exact suggestion model revision and sort mode.
- [x] Previous and Next controls navigate within the preserved queue on Windows and Pixel.
- [x] Accepting a suggestion advances to the next eligible face without returning to the gallery or skipping a face when queue membership changes.
- [x] Gallery cards expose the rank-one pending suggestion, score, margin and exact active model revision without per-card HTTP or database queries.
- [x] The queue can be ordered by suggested person, score margin, score and absence of suggestions, with stable deterministic tie-breaking.
- [ ] An operator can open one person and review every active assigned face with pagination and audit links.
- [ ] Person audit can surface assignments whose current top suggestion disagrees with the assigned person without changing canonical labels automatically.
- [ ] Suggestion-oriented bulk review groups likely same-person faces, previews the exact affected count and requires explicit confirmation.
- [ ] Bulk suggestion acceptance records both the normal assignment action and the linked suggestion-acceptance action for every affected face.
- [ ] The revised workflow remains touch-usable and privacy-limited on Windows and Pixel.
- [ ] Published-application smoke coverage protects person creation submission, queue navigation, auto-advance, suggestion summaries, person filtering and bulk suggestion acceptance.
- [ ] A fresh 50–100-face local queue records active review time, faces per minute, explicit actions per accepted suggestion, returns to the gallery and immediately undone decisions on both device types.

## Implemented slice: queue-aware details review

- Person creation on both the review and maintenance pages uses native form submission, so Enter and the mobile keyboard action share the same guarded create path as the Add button.
- Gallery and progress links carry a privacy-limited queue scope consisting of review state, optional processing run, optional exact model ID/hash, deterministic sort and a validated relative return URL.
- The filtered SQLite repository calculates Previous and Next IDs, one-based position and total from the same scope and order used by the queue.
- Details navigation uses server-calculated neighbour IDs rather than mutable offsets.
- Suggestion acceptance captures the next eligible face before mutation and navigates to it with browser-history replacement; accepting the last face returns to the originating queue.
- Integration coverage proves exact scope preservation, deterministic ordering, invalid-sort rejection, privacy boundaries and no skipped face after the current face leaves the unreviewed queue.

## Implemented slice: suggestion-aware gallery

- A dedicated Suggestions workspace selects one exact ranked-suggestion model revision and returns every card's rank-one pending suggestion in the same paged response.
- The SQLite query joins suggested person, score, margin and exact model provenance once per page; the browser performs no per-card suggestion request.
- Operators can order the queue by suggested person, high or low score margin, score, missing suggestion or creation time with deterministic face-ID tie-breaking.
- Cards support explicit acceptance through the existing audited suggestion endpoint, while ambiguous cases open an ordered quick-details queue with Previous, Next and automatic advance.
- The workspace can retain an optional processing-run scope while keeping faces with no suggestion visible.
- Integration coverage protects exact-model requirements, all ordering modes, mutation-stable queue navigation and privacy-limited responses.

The completed interaction criteria still require final Windows and Pixel verification as part of the cross-device acceptance gate.

## Delivery slices

1. Native person-creation form submission and queue-aware details navigation.
2. Top-suggestion summaries and suggestion-aware ordering in the gallery.
3. Per-person assigned-face audit.
4. Preview-first grouped suggestion acceptance with linked audit actions.
5. Local throughput measurement and Windows/Pixel verification.

## Safety boundary

Suggestion scores and grouping may reduce navigation and selection work, but no score or threshold may create a canonical label. Every individual or bulk acceptance remains an explicit human commit with normal audit semantics. New DTOs may expose image endpoints, opaque identifiers, scores and exact model provenance, but never source roots, crop paths or embeddings.
