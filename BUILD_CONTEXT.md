# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**WI-0163 Add safe bulk capture-date and Place enrichment is in progress under M31 Bulk archive metadata enrichment. WI-0091 Archive lifecycle review is also in progress under M23.**

The maintainer measured 17,892 current photos: 234 have no effective capture date, 12,603 have no named Place or valid non-zero GPS, and 12,371 of the location-less photos already have an effective date. Directory `1970` is a miscellaneous catch-all and must never be interpreted as a real capture year merely from its path.

WI-0163 remains on `agent/wi-0163-bulk-metadata-enrichment`. Its first CLI slice adds `metadata enrich`, which is dry-run by default, reads explicit JSON rules, proposes missing dates from conservative filename/path patterns, and proposes Places only when an effective date range is fully contained by an operator-supplied rule. `--apply` uses the existing PostgreSQL capture-date and Place repositories; existing effective dates, named Places and valid non-zero GPS are protected by default.

M28 remains completed. **M26 Creative Collections is completed.** M29 is ready with WI-0147 as its first PostgreSQL-only cleanup item. M30 video support remains intentionally blocked until explicit maintainer reactivation.

**M23 Source-copy lifecycle and privacy exclusion is in progress on its final item, WI-0091.** WI-0087, WI-0088, WI-0089 and WI-0090 are completed and maintainer-verified. WI-0091 is being implemented on `agent/wi-0091-archive-lifecycle-review`: a dedicated archive lifecycle workspace exposes Removed from source, Exact duplicates, Excluded, Purge pending and Purge failed review states; removed/duplicate copies can be selected for source-copy-specific bulk exclusion; completed exclusions are text/status-only with fresh re-inclusion; failed purges can be retried; and photo-viewer routes expose a confirmation-gated still-present privacy exclusion action. The backend bulk endpoint validates all selected revisions before mutating any exclusion state, and the exact-duplicate API filters excluded locators immediately.

## Next concrete step

Finish automated API/web validation for WI-0091, then run the complete M23 maintainer scenarios against the real PostgreSQL catalogue before closing WI-0091 and M23.

## Relevant files

- docs/delivery/milestones/M23-source-copy-lifecycle-and-exclusion.md
- docs/delivery/work-items/WI-0091-archive-lifecycle-review.md
- docs/delivery/status/work-items/active/WI-0091.yaml
- src/PhotoIdentity.Api/ArchiveItemFilterEndpoints.cs
- src/PhotoIdentity.Web/Pages/ArchiveLifecycle.razor
- src/PhotoIdentity.Web/Components/ExclusionList.razor
- src/PhotoIdentity.Web/Components/PhotoPrivacyExclusionAction.razor
- src/PhotoIdentity.Web/ArchiveContracts.cs
- docs/delivery/milestones/M31-bulk-metadata-enrichment.md
- docs/delivery/work-items/WI-0163-bulk-metadata-enrichment.md
- docs/delivery/status/work-items/active/WI-0163.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
