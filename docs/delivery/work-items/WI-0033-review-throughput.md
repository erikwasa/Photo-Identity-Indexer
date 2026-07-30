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

- [ ] Creating a person can be submitted with Enter on Windows and the mobile keyboard action on Pixel without duplicate creation.
- [ ] Face details preserve the originating review state, processing-run scope, exact suggestion model revision and sort mode.
- [ ] Previous and Next controls navigate within the preserved queue on Windows and Pixel.
- [ ] Accepting a suggestion advances to the next eligible face without returning to the gallery or skipping a face when queue membership changes.
- [ ] Gallery cards expose the rank-one pending suggestion, score, margin and exact active model revision without per-card HTTP or database queries.
- [ ] The queue can be ordered by suggested person, score margin, score and absence of suggestions, with stable deterministic tie-breaking.
- [ ] An operator can open one person and review every active assigned face with pagination and audit links.
- [ ] Person audit can surface assignments whose current top suggestion disagrees with the assigned person without changing canonical labels automatically.
- [ ] Suggestion-oriented bulk review groups likely same-person faces, previews the exact affected count and requires explicit confirmation.
- [ ] Bulk suggestion acceptance records both the normal assignment action and the linked suggestion-acceptance action for every affected face.
- [ ] The revised workflow remains touch-usable and privacy-limited on Windows and Pixel.
- [ ] Published-application smoke coverage protects person creation submission, queue navigation, auto-advance, suggestion summaries, person filtering and bulk suggestion acceptance.
- [ ] A fresh 50–100-face local queue records active review time, faces per minute, explicit actions per accepted suggestion, returns to the gallery and immediately undone decisions on both device types.

## Delivery slices

1. Native person-creation form submission and queue-aware details navigation.
2. Top-suggestion summaries and suggestion-aware ordering in the gallery.
3. Per-person assigned-face audit.
4. Preview-first grouped suggestion acceptance with linked audit actions.
5. Local throughput measurement and Windows/Pixel verification.

## Safety boundary

Suggestion scores and grouping may reduce navigation and selection work, but no score or threshold may create a canonical label. Every individual or bulk acceptance remains an explicit human commit with normal audit semantics. New DTOs may expose image endpoints, opaque identifiers, scores and exact model provenance, but never source roots, crop paths or embeddings.
