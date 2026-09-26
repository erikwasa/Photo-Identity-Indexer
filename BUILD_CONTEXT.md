# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**WI-0163 Add safe bulk capture-date and Place enrichment is in progress under M31 Bulk archive metadata enrichment.**

The maintainer measured 17,892 current photos: 234 have no effective capture date, 12,603 have no named Place or valid non-zero GPS, and 12,371 of the location-less photos already have an effective date. Directory `1970` is a miscellaneous catch-all and must never be interpreted as a real capture year merely from its path.

The implementation branch is `agent/wi-0163-bulk-metadata-enrichment`. The first CLI slice adds `metadata enrich`, which is dry-run by default, reads explicit JSON rules, proposes missing dates from conservative filename/path patterns, and proposes Places only when an effective date range is fully contained by an operator-supplied rule. `--apply` uses the existing PostgreSQL capture-date and Place repositories; existing effective dates, named Places and valid non-zero GPS are protected by default. Optional private reports contain per-photo source/revision details while normal stdout remains aggregate-only.

M28 remains completed. **M26 Creative Collections is completed.** WI-0162 closed the final M26 follow-on: the private-archive scale evaluation reached 10,585 indexed photos, English Visual/CLIP precision@10 was 0.89, English-only Visual search was accepted, and two saved search-result collections were verified through slideshow playback plus exact ordered-membership persistence after restart. M29 is ready with WI-0147 as its first PostgreSQL-only cleanup item. M30 video support remains intentionally blocked until explicit maintainer reactivation.

**M23 Source-copy lifecycle and privacy exclusion remains in progress.** WI-0089 completed and merged to `main` in PR #428 on 2026-09-25, establishing the durable exclusion/access boundary. WI-0087 is in review on `codex/wi-0087-verification-closeout`: automated SQLite, full-suite and live PostgreSQL verification is green, and only the privacy-safe maintainer real-catalogue duplicate check remains. WI-0088 also remains `in_review` pending the maintainer real-catalogue rename/move check. With WI-0089 completed, WI-0090's dependency gate is cleared and `PhotoIdentity.Docs next` can surface it even while its canonical status remains `proposed`; WI-0091 still depends on the remaining M23 sequence.

## Next concrete step

Have the maintainer verify one small real-catalogue WI-0087 exact-duplicate example without recording private filenames or hashes, then complete WI-0087 through `PhotoIdentity.Docs`. WI-0088's separate real-catalogue move/rename check remains pending.

## Relevant files

- docs/delivery/milestones/M31-bulk-metadata-enrichment.md
- docs/delivery/work-items/WI-0163-bulk-metadata-enrichment.md
- docs/delivery/status/work-items/active/WI-0163.yaml
- docs/delivery/milestones/M26-creative-collections.md
- docs/delivery/work-items/WI-0162-semantic-caption-search-slideshow-collections.md
- docs/delivery/status/work-items/archive/WI-0162.yaml
- docs/delivery/work-items/WI-0087-exact-duplicate-inventory.md
- docs/delivery/status/work-items/active/WI-0087.yaml
- tests/PhotoIdentity.Integration.Tests/ExactDuplicateRepositoryTests.cs
- src/PhotoIdentity.Cli/MetadataEnrichmentCommand.cs
- src/PhotoIdentity.Cli/Program.cs
- tests/PhotoIdentity.Integration.Tests/MetadataEnrichmentPlannerTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
