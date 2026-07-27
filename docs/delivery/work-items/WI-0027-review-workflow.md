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
- [ ] Safe bulk actions reduce repetitive assignment and rejection work while showing the affected count before commit.
- [ ] Review progress can be filtered by processing run, model revision and review state.
- [ ] Windows and Pixel trusted-network interaction remains usable with touch-sized controls and privacy-limited DTOs.
- [ ] Automated smoke coverage protects assignment, rejection, undo, suggestion review and person maintenance.

## Implemented slices

- Ranked suggestions expose score, margin and exact model provenance in the face-details view.
- Suggestion acceptance creates the normal append-only manual assignment; suggestion rejection creates a separately audited durable face-person exclusion.
- Matcher regeneration preserves rejected-pair exclusions.
- Person renames preserve old and new names and can be reversed through another audited rename.
- Person merges require explicit irreversible confirmation, retire the source identity and consolidate labels, reviewed assignments and suggestions into the surviving person.

## Safety boundary

No score or threshold automatically creates a canonical label. Every accepted identity remains an explicit human review action.
