# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**WI-0163 Add safe bulk capture-date and Place enrichment is in progress under M31 Bulk archive metadata enrichment.**

The maintainer measured 17,892 current photos: 234 have no effective capture date, 12,603 have no named Place or valid non-zero GPS, and 12,371 of the location-less photos already have an effective date. Directory `1970` is a miscellaneous catch-all and must never be interpreted as a real capture year merely from its path.

WI-0163 remains on `agent/wi-0163-bulk-metadata-enrichment`. Its first CLI slice adds `metadata enrich`, which is dry-run by default, reads explicit JSON rules, proposes missing dates from conservative filename/path patterns, and proposes Places only when an effective date range is fully contained by an operator-supplied rule. `--apply` uses the existing PostgreSQL capture-date and Place repositories; existing effective dates, named Places and valid non-zero GPS are protected by default.

M23 Source-copy lifecycle and privacy exclusion is completed with all five work items and the maintainer real-catalogue acceptance recorded. M28 and M26 remain completed. M29 is in progress with WI-0147 implemented and under review in PR #446; WI-0148 and WI-0149 remain the follow-up PostgreSQL-only cleanup items. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Continue WI-0163 from its canonical work-item/status shard. In parallel, complete maintainer review of WI-0147 / PR #446 before starting WI-0148.

## Relevant files

- docs/delivery/milestones/M31-bulk-metadata-enrichment.md
- docs/delivery/work-items/WI-0163-bulk-metadata-enrichment.md
- docs/delivery/status/work-items/active/WI-0163.yaml
- docs/delivery/work-items/WI-0147-postgres-only-runtime-composition.md
- docs/delivery/status/work-items/active/WI-0147.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
