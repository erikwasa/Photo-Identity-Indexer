---
id: WI-0027
title: Complete the local review workflow
milestone: M06
status_source: ../status/work-items.yaml
depends_on: [WI-0015, WI-0016]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests, PhotoIdentity.ReviewVerification]
---

# WI-0027: Complete the local review workflow

## Objective

Make the browser review application complete enough for sustained review of approximately 500 images on both Windows and Pixel.

## Acceptance criteria

- [x] Ranked identity suggestions are visible with score, margin and exact model revision.
- [x] An operator can accept or reject a suggestion without bypassing the append-only review history.
- [x] Rejected face-person pairs remain excluded after suggestion regeneration.
- [x] Person rename and merge are supported with auditable, reversible or explicitly irreversible semantics.
- [x] Safe bulk actions reduce repetitive assignment and rejection work while showing the affected count before commit.
- [x] Review progress can be filtered by processing run, model revision and review state.
- [ ] Windows and Pixel trusted-network interaction remains usable with touch-sized controls and privacy-limited DTOs.
- [x] Automated smoke coverage protects assignment, rejection, undo, suggestion review and person maintenance.

## Implemented slices

- Ranked suggestions expose score, margin and exact model provenance in the face-details view.
- Suggestion acceptance creates the normal append-only manual assignment; suggestion rejection creates a separately audited durable face-person exclusion.
- Matcher regeneration preserves rejected-pair exclusions.
- Person renames preserve old and new names and can be reversed through another audited rename.
- Person merges require explicit irreversible confirmation, retire the source identity and consolidate labels, reviewed assignments and suggestions into the surviving person.
- Bulk assignment and face rejection use a no-mutation preview, display affected and skipped counts, require explicit confirmation and reject stale previews atomically.
- Bulk commits preserve the normal append-only action history for every affected face and are bounded to 200 unique face IDs.
- A dedicated progress view combines review state, processing run and ranked-suggestion model revision filters.
- Processing-run scope is derived from jobs for the face's asset revision; model scope requires the exact model ID and full SHA-256 revision hash.
- Filter DTOs expose opaque run IDs, status, timestamps, exact model provenance and aggregate counts without source roots, crop paths or embeddings.
- The published-application smoke fixture now carries exact embeddings and deterministic test identities. `verify-review.ps1 -Mode Smoke` covers assignment plus undo, direct rejection, preview-first bulk assignment, suggestion accept and reject, reversible rename and explicitly irreversible merge against the hosted API.
- Suggestion rejection is asserted to leave the face unreviewed and create no canonical review action, while merge verification confirms the source identity is retired and its assignment moves to the surviving person.

## Safety boundary

No score or threshold automatically creates a canonical label. Every accepted identity remains an explicit human review action.
