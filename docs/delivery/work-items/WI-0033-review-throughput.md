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

The 500-image acceptance pilot completed successfully, but review was too slow on both device types. The first WI-0033 device run confirmed that the revised interaction flow is faster, while also identifying correctness defects and unnecessary page separation that must be fixed before the work item can close.

## Acceptance criteria

- [x] Creating a person can be submitted with Enter on Windows and the mobile keyboard action on Pixel without duplicate creation.
- [x] Face details preserve the originating review state, processing-run scope, exact suggestion model revision and sort mode.
- [x] Previous and Next controls navigate within the preserved queue on Windows and Pixel.
- [x] Accepting a suggestion advances to the next eligible face without returning to the gallery or skipping a face when queue membership changes.
- [x] Manual assignment from queue-aware details captures and advances to the next eligible face instead of losing queue navigation after mutation.
- [x] Creating a person from queue-aware details also assigns that person to the current face before advancing.
- [x] Gallery cards expose the rank-one pending suggestion, score, margin and exact active model revision without per-card HTTP or database queries.
- [x] The queue can be ordered by suggested person, score margin, score and absence of suggestions, with stable deterministic tie-breaking.
- [x] An operator can open one person and review every active assigned face with pagination and audit links.
- [x] Person audit can surface assignments whose current top suggestion disagrees with the assigned person without changing canonical labels automatically.
- [x] Suggestion-oriented bulk review groups likely same-person faces, previews the exact affected count and requires explicit confirmation.
- [x] Bulk suggestion acceptance records both the normal assignment action and the linked suggestion-acceptance action for every affected face.
- [ ] The revised workflow remains touch-usable and privacy-limited on Windows and Pixel after the corrective UI slices.
- [x] Published-application smoke coverage protects queue navigation, suggestion summaries, person filtering and bulk suggestion acceptance; person-creation keyboard submission, browser auto-advance and touch comfort remain explicit manual checks.
- [ ] A fresh 50–100-face local queue records active review time, faces per minute, explicit actions per accepted suggestion, returns to the gallery and immediately undone decisions on both device types.
- [x] Missing optional queue query values cannot crash Audit details or initial Progress loading.
- [x] Undoing an accepted suggestion restores the exact suggestion, score and margin to the active review queue.
- [x] Face details allow assignment to any active named person and create-and-assign for a new person.
- [x] Faces, suggestion ordering and grouped suggestion acceptance share one continuously loaded workspace; legacy suggestion URLs redirect to it.
- [x] The disposable interactive fixture contains more than one 40-card page so continuous loading can be manually exercised.
- [ ] The unified Faces and details views, including expanded model provenance, have no horizontal overflow on Pixel portrait orientation.

## Implemented slice: queue-aware details review

- Person creation on both the review and maintenance pages uses native form submission, so Enter and the mobile keyboard action share the same guarded create path as the Add button.
- Gallery and progress links carry a privacy-limited queue scope consisting of review state, optional processing run, optional exact model ID/hash, deterministic sort and a validated relative return URL.
- The filtered SQLite repository calculates Previous and Next IDs, one-based position and total from the same scope and order used by the queue.
- Details navigation uses server-calculated neighbour IDs rather than mutable offsets.
- Suggestion acceptance captures the next eligible face before mutation and navigates to it with browser-history replacement; accepting the last face returns to the originating queue.
- Integration coverage proves exact scope preservation, deterministic ordering, invalid-sort rejection, privacy boundaries and no skipped face after the current face leaves the unreviewed queue.

## Implemented slice: suggestion-aware gallery

- The exact-model suggestion query returns every card's rank-one pending suggestion, score, margin and model provenance in the same paged response.
- The SQLite query joins suggestion context once per page; the browser performs no per-card suggestion request.
- Operators can order the queue by suggested person, high or low score margin, score, missing suggestion or creation time with deterministic face-ID tie-breaking.
- Clear matches can be accepted directly from cards, while ambiguous cases use queue-aware details with Previous, Next and automatic advance.
- The queue can retain an optional processing-run scope while keeping faces with no suggestion visible.

## Implemented slice: per-person identity audit

- Audit lets the operator select one active person and page through every active assigned face.
- Each face links to append-only face audit history, where an incorrect assignment can be undone or corrected through the normal workflow.
- Exact-model comparison can show advisory rank-one disagreement counts, disagreement-only filtering and disagreement-first ordering.
- Rejected suggestions are excluded and no canonical label changes automatically.

## Implemented slice: grouped suggestion acceptance

- Rank-one pending matches can be selected for one suggested person and previewed before commit.
- Preview binds the requested suggestion IDs, common person, exact model revision and eligible face IDs into a deterministic token.
- Commit requires explicit confirmation, revalidates the token and applies the entire eligible set in one SQLite transaction.
- Every affected face receives a normal manual assignment action and a linked suggestion-acceptance action.
- Mixed-person groups, stale rank-one scope and changed eligibility are rejected without partial changes.

## Implemented slice: final verification preparation

- Published smoke exercises workflow routes, exact-model suggestion summaries, queue navigation metadata, person audit, grouped suggestion preview/commit, linked audit rows and privacy-limited responses.
- `record-review-session.ps1` records privacy-safe aggregate metrics for one Windows or Pixel session and creates a two-device summary after both reports exist.
- `docs/delivery/verification/WI-0033-manual-verification.md` defines like-for-like catalogue reset, trusted-network setup, metric definitions, mandatory failures and completion evidence.

## Corrective slice after device verification

The first real Windows and Pixel verification completed on 2026-07-31 and confirmed that manual review is improved. It also found correctness defects and mobile overflow in the separated suggestion surfaces.

- Query-bound state and sort values are normalized before URL encoding, preventing missing-query crashes from Audit and Progress.
- Undo restores a suggestion-backed assignment's exact suggestion to pending in the same transaction, preserving score and margin in the queue.
- One face-details page supports suggestion review, any-person assignment and inline person creation.
- Faces is the single review workspace for ordinary state review, suggestion context, suggestion-aware ordering and preview-first grouped acceptance.
- Results append through an intersection-observer sentinel instead of numbered pages.
- Cards hide image names, face ordinals, selection text and full model hashes; they retain state and concise top-suggestion evidence.
- Legacy Suggestions, Bulk suggestions and quick-details routes redirect into the unified workflow so saved links continue to work.
- Regression coverage protects accept-then-undo suggestion restoration.

## Focused follow-up from interactive verification

- Queue-aware manual assignment captures the current next-face ID before mutation and advances to that face after success; the final face returns to its originating queue.
- New-person details submission creates and assigns the person in one operator action before using the same advance rule. When details stay on the current face, the confirmation is shortened to `Created <name>`.
- Expanded active-model and per-suggestion provenance constrain the details element, timeline track and hash code to the available inline width, with break-all wrapping for uninterrupted digests.
- The top-suggestion card has more separation before the detector-confidence bar.
- The disposable fixture now prepares 64 faces, and smoke fails if the unreviewed set does not exceed one 40-card page.

WI-0033 remains `in_progress` until this follow-up is merged and manual assignment, create-and-assign, continuous loading and Pixel overflow are rerun. Only privacy-safe conclusions may be retained; local reports, photos, names, paths, databases, crops and embeddings remain local.

## Delivery slices

1. Native person-creation form submission and queue-aware details navigation.
2. Top-suggestion summaries and suggestion-aware ordering.
3. Per-person assigned-face audit.
4. Preview-first grouped suggestion acceptance with linked audit actions.
5. Published workflow smoke, local session reporting and Windows/Pixel manual verification.
6. Device-found defect correction and unified continuous review UI.
7. Queue-aware details auto-advance, expanded provenance containment and multi-page interactive fixture.

## Safety boundary

Suggestion scores and grouping may reduce navigation and selection work, but no score or threshold may create a canonical label. Every individual or grouped acceptance remains an explicit human commit with normal audit semantics. DTOs may expose image endpoints, opaque identifiers, scores and exact model provenance, but never source roots, crop paths or embeddings.
