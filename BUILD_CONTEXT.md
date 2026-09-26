# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**WI-0163 Add safe bulk capture-date and Place enrichment is in progress under M31 Bulk archive metadata enrichment.**

The maintainer measured 17,892 current photos: 234 have no effective capture date, 12,603 have no named Place or valid non-zero GPS, and 12,371 of the location-less photos already have an effective date. Directory `1970` is a miscellaneous catch-all and must never be interpreted as a real capture year merely from its path.

The implementation branch is `agent/wi-0163-bulk-metadata-enrichment`. The first CLI slice adds `metadata enrich`, which is dry-run by default, reads explicit JSON rules, proposes missing dates from conservative filename/path patterns, and proposes Places only when an effective date range is fully contained by an operator-supplied rule. `--apply` uses the existing PostgreSQL capture-date and Place repositories; existing effective dates, named Places and valid non-zero GPS are protected by default. Optional private reports contain per-photo source/revision details while normal stdout remains aggregate-only.

M28 remains completed. **M26 Creative Collections is completed.** WI-0162 closed the final M26 follow-on: the private-archive scale evaluation reached 10,585 indexed photos, English Visual/CLIP precision@10 was 0.89, English-only Visual search was accepted, and two saved search-result collections were verified through slideshow playback plus exact ordered-membership persistence after restart. M29 is ready with WI-0147 as its first PostgreSQL-only cleanup item. M30 video support remains intentionally blocked until explicit maintainer reactivation.

**M23 Source-copy lifecycle and privacy exclusion is ready, with only WI-0091 left.** WI-0087, WI-0088, WI-0089 and WI-0090 are completed. On 2026-09-26 the maintainer verified WI-0087 against the permanent PostgreSQL catalogue: the exact-duplicate endpoint completed normally, returned one group containing 1,878 independently catalogued copies, and inspected copies were genuine while retaining different AssetIds. The maintainer also verified WI-0088 by renaming one included local photo: synchronization reported `reconciled_moves=1`, preserved the AssetId and existing history, and a second synchronization reported `reconciled_moves=0`. WI-0091 is now fully unblocked and owns the remaining operator-facing lifecycle review/exclusion workflow for M23.

## Next concrete step

Continue WI-0163 under M31. When returning to M23, start WI-0091; all of its source-copy lifecycle dependencies are now completed and maintainer-verified.

## Relevant files

- docs/delivery/milestones/M31-bulk-metadata-enrichment.md
- docs/delivery/work-items/WI-0163-bulk-metadata-enrichment.md
- docs/delivery/status/work-items/active/WI-0163.yaml
- docs/delivery/milestones/M26-creative-collections.md
- docs/delivery/work-items/WI-0162-semantic-caption-search-slideshow-collections.md
- docs/delivery/status/work-items/archive/WI-0162.yaml
- docs/delivery/work-items/WI-0087-exact-duplicate-inventory.md
- docs/delivery/status/work-items/archive/WI-0087.yaml
- docs/delivery/work-items/WI-0088-source-move-reconciliation.md
- docs/delivery/status/work-items/archive/WI-0088.yaml
- docs/delivery/work-items/WI-0091-archive-lifecycle-review.md
- tests/PhotoIdentity.Integration.Tests/ExactDuplicateRepositoryTests.cs
- tests/PhotoIdentity.Integration.Tests/SourceMoveReconciliationTests.cs
- src/PhotoIdentity.Cli/MetadataEnrichmentCommand.cs
- src/PhotoIdentity.Cli/Program.cs
- tests/PhotoIdentity.Integration.Tests/MetadataEnrichmentPlannerTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
