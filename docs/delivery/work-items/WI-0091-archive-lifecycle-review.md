---
id: WI-0091
title: Add archive lifecycle review and exclusion workflows
milestone: M23
status_source: ../status/work-items.yaml
depends_on: [WI-0087, WI-0088, WI-0089, WI-0090]
related_adrs: [ADR-0008]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Web, documentation]
---

# WI-0091: Add archive lifecycle review and exclusion workflows

## Objective

Give the operator clear, efficient UI for reviewing removed source photos, exact duplicate copies and privacy exclusions, including bulk **Exclude & purge** for selected source copies.

## Why

The expected archive workflow includes deleting low-quality/inappropriate/burst photos directly in OneDrive and separately excluding private still-present photos from Photo Identity. Those actions need discoverable review surfaces rather than database-only lifecycle states.

## In scope

- Add/archive-filter operator states for **Removed from source**, **Exact duplicates**, **Excluded**, **Purge pending** and **Purge failed**.
- Present removed-source photos with retained review proxies while they have not yet been excluded/purged.
- Support multi-select **Exclude & purge selected** for removed-source entries.
- Present exact-duplicate groups with every source copy independently selectable; never imply that selecting one copy affects the others.
- Add **Exclude from Photo Identity** for a still-present photo from an appropriate archive/photo-details surface.
- Confirmation copy must state that the OneDrive/source original is not deleted and that Photo Identity's local photo/face/identity/metadata data will be permanently removed.
- Remove thumbnail/original/view actions after purge completes; Excluded becomes a text/status-only archive entry.
- Show actionable retry state for purge pending/failed without exposing private paths in errors.
- Provide explicit re-include/restore for a purged excluded locator; restoration re-catalogues/re-analyzes from source rather than restoring deleted identity data.
- Keep normal missing-source handling non-destructive until the operator explicitly chooses exclusion/purge.
- Preserve navigation/context after bulk actions where practical.

## Out of scope

- A one-click exclude-all-duplicates content-level action.
- Perceptual/near-duplicate review.
- Deleting source originals from the application.
- Manual resolution UI for ambiguous move candidates unless implementation evidence shows it is necessary for the milestone's accepted scenarios.

## Acceptance criteria

- [ ] Archive exposes Removed from source, Exact duplicates, Excluded and purge-problem states with useful counts/filtering.
- [ ] Removed-from-source entries retain enough preview context for the operator to decide until exclusion/purge is chosen.
- [ ] Multiple removed entries can be selected and excluded/purged in one operator action.
- [ ] Duplicate groups show each source copy independently; excluding one leaves another identical source path included.
- [ ] A still-present photo can be manually excluded with clear non-source-deletion warning.
- [ ] Once exclusion starts, the photo disappears from normal library/review/collection/slideshow surfaces immediately.
- [ ] After purge completes, Excluded shows no thumbnail or original-view action.
- [ ] Purge pending/failed entries remain blocked and offer retry/actionable status.
- [ ] Restore/re-include starts fresh processing and does not restore purged identifications/history.
- [ ] Maintainer acceptance proves: duplicate A/B exclude A only; included rename preserves identity; excluded rename appears as a new included copy; OneDrive deletion enters Removed from source; bulk removed-source purge works; still-present private photo leaves no local Photo Identity derivative/identity data after purge.

## Verification requirements

Automated web/API integration tests should cover state/filter/action contracts and permission/access behavior. Maintainer real-catalogue verification is required for the complete scenarios above, using privacy-safe evidence only.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
