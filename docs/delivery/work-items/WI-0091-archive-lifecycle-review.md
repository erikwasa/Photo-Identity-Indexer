---
id: WI-0091
title: Add archive lifecycle review and exclusion workflows
milestone: M23
status_source: ../status/work-items.yaml
depends_on: [WI-0087, WI-0088, WI-0089, WI-0090]
related_adrs: [ADR-0008]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, documentation]
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

- [x] Archive exposes Removed from source, Exact duplicates, Excluded and purge-problem states with useful counts/filtering.
- [x] Removed-from-source entries retain enough preview context for the operator to decide until exclusion/purge is chosen.
- [x] Multiple removed entries can be selected and excluded/purged in one operator action.
- [x] Duplicate groups show each source copy independently; excluding one leaves another identical source path included.
- [x] A still-present photo can be manually excluded with clear non-source-deletion warning.
- [x] Once exclusion starts, the photo disappears from normal library/review/collection/slideshow surfaces immediately.
- [x] After purge completes, Excluded shows no thumbnail or original-view action.
- [x] Purge pending/failed entries remain blocked and offer retry/actionable status.
- [x] Restore/re-include starts fresh processing and does not restore purged identifications/history.
- [ ] Maintainer acceptance proves: duplicate A/B exclude A only; included rename preserves identity; excluded rename appears as a new included copy; OneDrive deletion enters Removed from source; bulk removed-source purge works; still-present private photo leaves no local Photo Identity derivative/identity data after purge.

## Verification requirements

Automated web/API integration tests should cover state/filter/action contracts and permission/access behavior. Maintainer real-catalogue verification is required for the complete scenarios above, using privacy-safe evidence only.

### Maintainer verification helper

`verify-wi0091.ps1` is a privacy-safe staged helper for the real-catalogue acceptance. It does not delete source files or perform exclusion/restore actions itself. It records private revision/source locators only under the ignored `artifacts/` directory, while normal stdout reports counts and pass/fail state. Use `-ShowPrivateDetails` only when local source paths are useful for choosing a disposable test photo.

Typical flow:

```powershell
.\verify-wi0091.ps1 -Stage Preflight -RunPostgres
.\verify-wi0091.ps1 -Stage ListDuplicates -ShowPrivateDetails
.\verify-wi0091.ps1 -Stage RecordDuplicate -DuplicateGroup 1 -DuplicateCopy 1
# Exclude that recorded copy in /archive/lifecycle, then:
.\verify-wi0091.ps1 -Stage VerifyDuplicate
.\verify-wi0091.ps1 -Stage WaitForPurge -Target duplicate

.\verify-wi0091.ps1 -Stage ListRemoved -ShowPrivateDetails
.\verify-wi0091.ps1 -Stage RecordRemoved -RemovedRows 1,2
# Bulk Exclude & purge the same rows in /archive/lifecycle, then:
.\verify-wi0091.ps1 -Stage VerifyRemoved
.\verify-wi0091.ps1 -Stage WaitForPurge -Target removed

# While viewing a disposable still-present photo, copy its /photo/<revision> URL:
.\verify-wi0091.ps1 -Stage RecordPhoto -Photo '<photo URL>'
# Use Exclude from Photo Identity in the viewer, then:
.\verify-wi0091.ps1 -Stage VerifyPhotoExclusion
.\verify-wi0091.ps1 -Stage WaitForPurge -Target photo
# Re-include from the Excluded view, then:
.\verify-wi0091.ps1 -Stage VerifyRestore
```

For the source-deletion scenario, record the disposable still-present photo before deleting it from OneDrive/source, synchronize, then verify it entered the non-destructive Removed queue:

```powershell
.\verify-wi0091.ps1 -Stage RecordPhoto -Photo '<photo URL>'
# delete the disposable source copy outside Photo Identity
.\verify-wi0091.ps1 -Stage Sync
.\verify-wi0091.ps1 -Stage VerifyPhotoRemoved
```

For the excluded-rename scenario, record and exclude a disposable still-present photo, wait for purge completion, rename/move its source original outside Photo Identity, synchronize, and pass the new archive-relative source key locally:

```powershell
.\verify-wi0091.ps1 -Stage Sync
.\verify-wi0091.ps1 -Stage VerifyExcludedRename -NewSourceKey '<new archive-relative path>'
```

The already accepted WI-0088 real-catalogue evidence for an included rename (same AssetId, `reconciled_moves=1`, second sync `reconciled_moves=0`) satisfies the included-rename part of the final M23 acceptance and does not need to be repeated.

## Completion notes

- Files changed: PR #438 adds the dedicated `/archive/lifecycle` workspace, lifecycle API actions/contracts, review-navigation entry, a route-aware photo privacy exclusion action, focused application integration tests and the `verify-wi0091.ps1` maintainer helper.
- State model: Removed from source, Exact duplicates, Excluded, Purge pending and Purge failed are separate operator surfaces. Excluded locators are removed immediately from exact-duplicate and removed-source review results while the lower-level exclusion boundary blocks normal media/query/processing access.
- Bulk safety: the bulk endpoint parses and resolves every selected revision before creating any tombstone, so an invalid/stale selection cannot leave a partially excluded batch.
- Privacy UX: destructive confirmations explicitly say the OneDrive/source original is not deleted. Completed exclusions are text/status-only; failed purge exposes only the stored privacy-safe error code and a retry action.
- Re-inclusion: only a completed purge may be restored. The UI removes the tombstone and triggers archive synchronization so any still-present source copy is catalogued again from source; purged revision-linked identities/history are not reconstructed.
- Automated coverage: focused API tests cover atomic bulk validation, independent duplicate-copy exclusion, immediate withdrawal from duplicate and removed review queues, failed-purge retry, completed-purge restore gating and removed-source revision retention.
- Trade-offs: the lifecycle UI uses the existing durable review proxy endpoint for recognizable removed-photo previews rather than introducing a second preview store. Exact-duplicate copies are paged in the browser after the provider query because WI-0087 already owns the indexed authoritative duplicate inventory.
- Deferred work: the final checkbox remains the maintainer real-catalogue acceptance sequence for M23. WI-0091 and M23 remain in progress until that evidence is recorded.
- Commands run: GitHub Actions is the automated gate for PR #438; use `verify-wi0091.ps1` for live PostgreSQL and privacy-safe real-catalogue acceptance before completion.
